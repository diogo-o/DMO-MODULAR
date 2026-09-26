using System.Net;
using System.Text.Json;
using DMO.Application.Access;
using DMO.Application.Controlo.Pesos;
using DMO.Application.Controlo.Settings;
using DMO.Domain.Tools;
using DMO.IntegrationTests.Controlo.Pesos;
using DMO.IntegrationTests.JobOn;
using DMO.IntegrationTests.Navigation;

namespace DMO.IntegrationTests.Controlo.Settings;

/// <summary>
/// P2-T05 post-closure correction — transport-class (<c>I</c>) proofs of the glass-density
/// settings surface (correction contract §5.3, routes 18/19) and of the frozen-history guarantee
/// (Owner rule R3–R6): the REAL web host, the REAL <c>ControloDefinicoesEndpoints</c> and the
/// REAL <c>ControloDefinicoesService</c>/<c>ControloCreateService</c> over the controlled P2-T05
/// store (seeded with the provenance-backed bootstrap values NNPB 2.4027 / PS 2.4231).
/// </summary>
/// <remarks>
/// Authority: correction contract §5.3 (routes/refusals), §5.4 (calculation source + freeze),
/// §5.7 rows U/I/R and §6.2 (closed-decision preservation). Every guarded write is
/// version-guarded with the existing D2 conflict/reload semantics; an approve-only caller is
/// denied server-side.</remarks>
public sealed class GlassDensitySettingsEndpointsTests
{
    private const string DefinicoesPath = "/controlo/create/definicoes";
    private const string GlassDensitiesPath = $"{DefinicoesPath}/glass-densities";

    // -----------------------------------------------------------------------------------------
    // Route 18 — GET
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// GD-I1 — GET returns ALWAYS exactly the two current operational values in canonical order
    /// (NNPB, PS) with their versions, starting from the provenance-backed bootstrap values
    /// (2.4027 / 2.4231 g/cm³).
    /// </summary>
    [Fact]
    public async Task GD_I1_GetReturnsExactlyTheTwoCurrentOperationalValues()
    {
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T05TestHost.GetAsync(client, GlassDensitiesPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await ReadJsonAsync(response);
        var items = payload.GetProperty("glassDensities").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);

        Assert.Equal("NNPB", items[0].GetProperty("processo").GetString());
        Assert.Equal(2.4027m, items[0].GetProperty("densityGcm3").GetDecimal());
        Assert.Equal(1, items[0].GetProperty("version").GetInt32());

        Assert.Equal("PS", items[1].GetProperty("processo").GetString());
        Assert.Equal(2.4231m, items[1].GetProperty("densityGcm3").GetDecimal());
        Assert.Equal(1, items[1].GetProperty("version").GetInt32());
    }

    // -----------------------------------------------------------------------------------------
    // Route 19 — PUT
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// GD-I2 — PUT updates ONLY the targeted processo's row: the NNPB value changes (version 2)
    /// while the PS row keeps its value AND version; the GET surface mirrors exactly that state.
    /// </summary>
    [Fact]
    public async Task GD_I2_PutUpdatesOnlyTheTargetedProcessoIndependently()
    {
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using (var updated = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{GlassDensitiesPath}/NNPB",
                   P2T05TestHost.Json(new { densityGcm3 = 2.41m, expectedVersion = 1 })))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

            var payload = await ReadJsonAsync(updated);
            Assert.Equal("NNPB", payload.GetProperty("processo").GetString());
            Assert.Equal(2.41m, payload.GetProperty("densityGcm3").GetDecimal());
            Assert.Equal(2, payload.GetProperty("version").GetInt32());
        }

        using (var list = await P2T05TestHost.GetAsync(client, GlassDensitiesPath))
        {
            var payload = await ReadJsonAsync(list);
            var items = payload.GetProperty("glassDensities").EnumerateArray().ToArray();

            Assert.Equal("NNPB", items[0].GetProperty("processo").GetString());
            Assert.Equal(2.41m, items[0].GetProperty("densityGcm3").GetDecimal());
            Assert.Equal(2, items[0].GetProperty("version").GetInt32());

            // PS is untouched: same value, same version.
            Assert.Equal("PS", items[1].GetProperty("processo").GetString());
            Assert.Equal(2.4231m, items[1].GetProperty("densityGcm3").GetDecimal());
            Assert.Equal(1, items[1].GetProperty("version").GetInt32());
        }

        // The other direction works independently too.
        using (var updated = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{GlassDensitiesPath}/PS",
                   P2T05TestHost.Json(new { densityGcm3 = 2.4231m, expectedVersion = 1 })))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal(2, (await ReadJsonAsync(updated)).GetProperty("version").GetInt32());
        }
    }

    /// <summary>
    /// GD-I3 — a stale version (observed 1, current 2) is refused with 409 <c>stale-version</c>
    /// and NOTHING is written: the row keeps the newer value and version (D2 conflict/reload
    /// recovery on the frontend).
    /// </summary>
    [Fact]
    public async Task GD_I3_AStaleVersionIsRefusedWithConflictAndNothingIsWritten()
    {
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        // First write: version 1 → 2.
        using (var first = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{GlassDensitiesPath}/NNPB",
                   P2T05TestHost.Json(new { densityGcm3 = 2.41m, expectedVersion = 1 })))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        // Stale write: observed version 1, current version 2 → refusal, nothing written.
        using (var stale = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{GlassDensitiesPath}/NNPB",
                   P2T05TestHost.Json(new { densityGcm3 = 2.42m, expectedVersion = 1 })))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            var payload = await ReadJsonAsync(stale);
            Assert.Equal("stale-version", payload.GetProperty("reason").GetString());
        }

        using (var list = await P2T05TestHost.GetAsync(client, GlassDensitiesPath))
        {
            var payload = await ReadJsonAsync(list);
            var nnpb = payload.GetProperty("glassDensities")[0];
            Assert.Equal(2.41m, nnpb.GetProperty("densityGcm3").GetDecimal());
            Assert.Equal(2, nnpb.GetProperty("version").GetInt32());
        }
    }

    /// <summary>
    /// GD-I4 — an unknown processo is refused with 400 <c>PROCESSO_UNKNOWN</c> (no alias, no
    /// other processo) and a zero/negative density with 400 <c>DENSITY_NOT_POSITIVE</c>; nothing
    /// is ever written by a refused update.
    /// </summary>
    [Fact]
    public async Task GD_I4_UnknownProcessoAndNonPositiveDensityAreRefusedWithTheExactTokens()
    {
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        foreach (var processo in new[] { "TOOL", "npnb", "PSP", "B1" })
        {
            using var unknown = await P2T05TestHost.SendJsonAsync(
                client,
                HttpMethod.Put,
                $"{GlassDensitiesPath}/{processo}",
                P2T05TestHost.Json(new { densityGcm3 = 2.5m, expectedVersion = 1 }));

            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

            var payload = await ReadJsonAsync(unknown);
            Assert.Equal("validation-failed", payload.GetProperty("reason").GetString());
            Assert.Contains(
                ControloDefinicoesValidationErrors.ProcessoUnknown,
                payload.GetProperty("errors").EnumerateArray().Select(error => error.GetString() ?? string.Empty));
        }

        foreach (var density in new[] { 0m, -1m })
        {
            using var notPositive = await P2T05TestHost.SendJsonAsync(
                client,
                HttpMethod.Put,
                $"{GlassDensitiesPath}/NNPB",
                P2T05TestHost.Json(new { densityGcm3 = density, expectedVersion = 1 }));

            Assert.Equal(HttpStatusCode.BadRequest, notPositive.StatusCode);

            var payload = await ReadJsonAsync(notPositive);
            Assert.Equal("validation-failed", payload.GetProperty("reason").GetString());
            Assert.Contains(
                ControloDefinicoesValidationErrors.DensityNotPositive,
                payload.GetProperty("errors").EnumerateArray().Select(error => error.GetString() ?? string.Empty));
        }

        // The whole surface is untouched by the refused writes.
        using (var list = await P2T05TestHost.GetAsync(client, GlassDensitiesPath))
        {
            var payload = await ReadJsonAsync(list);
            var items = payload.GetProperty("glassDensities").EnumerateArray().ToArray();
            Assert.Equal(2.4027m, items[0].GetProperty("densityGcm3").GetDecimal());
            Assert.Equal(2.4231m, items[1].GetProperty("densityGcm3").GetDecimal());
        }
    }

    /// <summary>
    /// GD-I5 — the routes stay gated by EXACTLY <c>dmo.module.controlo-create</c>: an
    /// approve-only caller is denied both the read and the write with 403 (AC-G2) and nothing is
    /// written.
    /// </summary>
    [Fact]
    public async Task GD_I5_TheGlassDensityRoutesStayControloCreateGated()
    {
        var approve = TestNavigationComposition.Definition(
            ModuleCatalog.ControloApprove, "Controlo Approve", "controlo", "Controlo");
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser([approve], composition);
        using var client = factory.CreateClient();

        using (var read = await P2T05TestHost.GetAsync(client, GlassDensitiesPath))
        {
            Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        }

        using (var write = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{GlassDensitiesPath}/NNPB",
                   P2T05TestHost.Json(new { densityGcm3 = 2.5m, expectedVersion = 1 })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        }
    }

    // -----------------------------------------------------------------------------------------
    // Frozen history end-to-end (Owner rule R3–R6, correction contract §5.4)
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// GD-I6 — the full Owner-rule chain: cm_id → tool_id → processo → CURRENT settings value →
    /// frozen on the Peso. Saving a Peso freezes the current NNPB value (2.4027); after the
    /// Definições value changes the EXISTING Peso keeps its frozen density and frozen per-row
    /// results (recalculation and submit re-derive with the frozen value), while a NEW Peso uses
    /// the new value. No historical Peso is ever rewritten.
    /// </summary>
    [Fact]
    public async Task GD_I6_NewPesosUseTheCurrentValueAndExistingPesosKeepTheirFrozenDensity()
    {
        var composition = new P2T05TestComposition();
        var tool = composition.SeedTool(ToolType.Cm, "5447T173", "LOTE-GD", Processo.Nnpb);
        var jobOn = composition.SeedJobOnWithCmContext(
            "GD-TEST", "1000", "B1", tool.ToolId.Value, ToolType.Cm, tool.Reference, tool.Lot);
        var cmId = jobOn.Contexts.Single(context => context.ContextType == ToolContextType.Cm).ContextId;

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        // 1) Save a Peso while NNPB = 2.4027 (seeded current value).
        Guid pesoId;
        using (var created = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   "/controlo/create/pesos",
                   P2T05TestHost.Json(new
                   {
                       cmId,
                       pendingToolId = (Guid?)null,
                       waterTemperature = 25m,
                       volumeMarisaBq = (decimal?)null,
                       volumePuncaoPu = (decimal?)null,
                       previousProductionEndReference = (string?)null,
                       previousAverageWeightReference = (string?)null,
                       rows = new[] { new { waterWeightG = 997.1m } },
                   })))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            pesoId = (await ReadJsonAsync(created)).GetProperty("pesoId").GetGuid();
        }

        // 2) The Definições value changes NNPB → 3.00.
        using (var changed = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{GlassDensitiesPath}/NNPB",
                   P2T05TestHost.Json(new { densityGcm3 = 3.00m, expectedVersion = 1 })))
        {
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        }

        // 3) The EXISTING Peso keeps its frozen density and per-row results (never rewritten).
        decimal frozenCapacity;
        decimal frozenGlass;
        using (var sheet = await P2T05TestHost.GetAsync(client, $"/controlo/create/pesos/{pesoId}"))
        {
            Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);

            var payload = await ReadJsonAsync(sheet);
            Assert.Equal(2.4027m, payload.GetProperty("glassDensityGCm3").GetDecimal());
            frozenCapacity = payload.GetProperty("rows")[0].GetProperty("capacityCm3").GetDecimal();
            frozenGlass = payload.GetProperty("rows")[0].GetProperty("glassWeightG").GetDecimal();
            Assert.Equal(1000m, frozenCapacity); // 997.1 ÷ 0.9971
            Assert.Equal(2402.7000m, frozenGlass); // 1000 × 2.4027 — the frozen value
        }

        // 4) Recalculation of the SAME Peso keeps using the FROZEN density.
        using (var recalculated = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"/controlo/create/pesos/{pesoId}",
                   P2T05TestHost.Json(new
                   {
                       expectedVersion = 1,
                       waterTemperature = 25m,
                       volumeMarisaBq = (decimal?)null,
                       volumePuncaoPu = (decimal?)null,
                       previousProductionEndReference = (string?)null,
                       previousAverageWeightReference = (string?)null,
                       rows = new[] { new { waterWeightG = 997.1m } },
                   })))
        {
            Assert.Equal(HttpStatusCode.OK, recalculated.StatusCode);
        }

        using (var afterEdit = await P2T05TestHost.GetAsync(client, $"/controlo/create/pesos/{pesoId}"))
        {
            var payload = await ReadJsonAsync(afterEdit);
            Assert.Equal(2.4027m, payload.GetProperty("glassDensityGCm3").GetDecimal());
            Assert.Equal(frozenGlass, payload.GetProperty("rows")[0].GetProperty("glassWeightG").GetDecimal());
        }

        // 5) Submit recompute-verify passes only because it re-derives with the FROZEN density.
        using (var submitted = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"/controlo/create/pesos/{pesoId}/submit",
                   P2T05TestHost.Json(new { expectedVersion = 2 })))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        }

        // 6) A NEW Peso resolves the NEW current value (3.00) and freezes it.
        using (var laterCreated = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   "/controlo/create/pesos",
                   P2T05TestHost.Json(new
                   {
                       cmId,
                       pendingToolId = (Guid?)null,
                       waterTemperature = 25m,
                       volumeMarisaBq = (decimal?)null,
                       volumePuncaoPu = (decimal?)null,
                       previousProductionEndReference = (string?)null,
                       previousAverageWeightReference = (string?)null,
                       rows = new[] { new { waterWeightG = 997.1m } },
                   })))
        {
            Assert.Equal(HttpStatusCode.Created, laterCreated.StatusCode);
            var laterPesoId = (await ReadJsonAsync(laterCreated)).GetProperty("pesoId").GetGuid();

            using var laterSheet = await P2T05TestHost.GetAsync(client, $"/controlo/create/pesos/{laterPesoId}");
            var laterPayload = await ReadJsonAsync(laterSheet);
            Assert.Equal(3.00m, laterPayload.GetProperty("glassDensityGCm3").GetDecimal());
            Assert.Equal(1000m, laterPayload.GetProperty("rows")[0].GetProperty("capacityCm3").GetDecimal()); // water unchanged
            Assert.Equal(3000.0000m, laterPayload.GetProperty("rows")[0].GetProperty("glassWeightG").GetDecimal());
        }
    }

    // ---- arrangement helpers -------------------------------------------------------------

    /// <summary>Reads a JSON response body into a detached element.</summary>
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}