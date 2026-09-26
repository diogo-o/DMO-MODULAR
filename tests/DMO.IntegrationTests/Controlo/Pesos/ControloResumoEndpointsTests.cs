using System.Net;
using System.Text.Json;
using DMO.Application.Access;
using DMO.Domain.Tools;
using DMO.IntegrationTests.JobOn;

namespace DMO.IntegrationTests.Controlo.Pesos;

/// <summary>
/// Owner-clarification delta (P2-T05 §31.1) — transport-class proofs of the Resumo da produção
/// entry read: the REAL web host with the REAL endpoints over the controlled stores.
/// </summary>
/// <remarks>
/// The route <c>GET /controlo/create/resumo/{jobonId}</c> is the Controlo entry read of one
/// occurrence (jobon_id → Resumo da produção → cm_id → Peso): it carries the production facts and
/// the CM context the Peso surface is populated from — the light packet, never the full ficha and
/// never any Peso/output/history payload. Grant holders receive the projection; an unknown
/// occurrence is <c>404 not-found</c>; a caller without <c>controlo-create</c> is denied server-side.
/// </remarks>
public sealed class ControloResumoEndpointsTests
{
    private const string CreatePath = "/controlo/create";

    /// <summary>
    /// RESUMO1 — a granted caller receives the Resumo da produção of a seeded occurrence: the
    /// production facts (reference, production number, machine, date) plus the CM context (real
    /// cm_id/tool_id, frozen triple and the live entry facts — reference, lote, processo, quantity).
    /// </summary>
    [Fact]
    public async Task RESUMO1_ResumoDaProducaoComContextoCmChegaAoChamadorAutorizado()
    {
        var composition = new P2T05TestComposition();
        var tool = composition.SeedTool(
            ToolType.Cm, "5447T173", "LOTE-R1", Processo.Nnpb, quantity: 9, "B1");
        var jobOn = composition.SeedJobOnWithCmContext(
            "R-REF-1", "3001", "B1", tool.ToolId.Value, ToolType.Cm, tool.Reference, tool.Lot,
            new DateOnly(2026, 4, 2));

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T05TestHost.GetAsync(
            client, $"{CreatePath}/resumo/{jobOn.JobOnId.Value}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;

        Assert.Equal(jobOn.JobOnId.Value, root.GetProperty("jobonId").GetGuid());
        Assert.Equal("R-REF-1", root.GetProperty("reference").GetString());
        Assert.Equal("3001", root.GetProperty("productionNumber").GetString());
        Assert.Equal("B1", root.GetProperty("machine").GetString());
        Assert.Equal("2026-04-02", root.GetProperty("productionDate").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());

        var cm = root.GetProperty("cm");
        Assert.Equal(JsonValueKind.Object, cm.ValueKind);
        Assert.Equal(tool.ToolId.Value, cm.GetProperty("toolId").GetGuid());
        Assert.Equal("CM", cm.GetProperty("frozenToolType").GetString());
        Assert.Equal("5447T173", cm.GetProperty("frozenToolReference").GetString());
        Assert.Equal("LOTE-R1", cm.GetProperty("frozenToolLot").GetString());
        Assert.Equal("5447T173", cm.GetProperty("liveToolReference").GetString());
        Assert.Equal("LOTE-R1", cm.GetProperty("liveToolLot").GetString());
        Assert.Equal("NNPB", cm.GetProperty("processo").GetString());
        Assert.Equal(9, cm.GetProperty("quantity").GetInt32());

        // The cm_id is present so the Peso surface anchors on it without re-entering context.
        Assert.NotEqual(Guid.Empty, cm.GetProperty("cmId").GetGuid());
    }

    /// <summary>
    /// RESUMO1 — a production WITHOUT a CM context is still a valid Resumo: the production facts
    /// arrive and <c>cm</c> is the truthful <c>null</c> (the missing-context recovery stays the
    /// operator's explicit path).
    /// </summary>
    [Fact]
    public async Task RESUMO1_ProducaoSemContextoCmTrazFactosECmNulo()
    {
        var composition = new P2T05TestComposition();
        var jobOn = composition.JobOnToolStore.SeedJobOn("R-REF-2", "3002", "C2");

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T05TestHost.GetAsync(
            client, $"{CreatePath}/resumo/{jobOn.JobOnId.Value}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;

        Assert.Equal("R-REF-2", root.GetProperty("reference").GetString());
        Assert.Equal("3002", root.GetProperty("productionNumber").GetString());
        Assert.Equal("C2", root.GetProperty("machine").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("cm").ValueKind);
    }

    /// <summary>
    /// RESUMO2 — an unknown jobon_id is <c>404 not-found</c> (never an invented summary, never an
    /// empty 200).
    /// </summary>
    [Fact]
    public async Task RESUMO2_JobOnDesconhecidoDevolveNotFound()
    {
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T05TestHost.GetAsync(
            client, $"{CreatePath}/resumo/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("not-found", payload.RootElement.GetProperty("reason").GetString());
    }

    /// <summary>
    /// RESUMO3 — the entry read is server-gated: a caller without <c>controlo-create</c> is denied
    /// with 403 (the shared <c>controlo</c> destination never merges grants).
    /// </summary>
    [Fact]
    public async Task RESUMO3_ChamadorSemControloCreateENegado()
    {
        var composition = new P2T05TestComposition();
        var jobOn = composition.JobOnToolStore.SeedJobOn("R-REF-3", "3003", "B1");

        using var factory = P2T05TestHost.ForUser(Array.Empty<ModuleDefinition>(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T05TestHost.GetAsync(
            client, $"{CreatePath}/resumo/{jobOn.JobOnId.Value}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// RESUMO4 — the Novo controlo page opened for a production enters THROUGH the Resumo: the
    /// rendered strip carries reference/production/machine/processo, the CM region the frozen triple
    /// and the Peso anchor region is pre-anchored on the real cm_id — the operator supplies only the
    /// Peso-workflow facts, never the production context.
    /// </summary>
    [Fact]
    public async Task RESUMO4_PaginaAbertaParaAProducaoEntraPeloResumoEAncoraNoCm()
    {
        var composition = new P2T05TestComposition();
        var tool = composition.SeedTool(
            ToolType.Cm, "5447T174", "LOTE-R4", Processo.Ps, quantity: 4, "B1");
        var jobOn = composition.SeedJobOnWithCmContext(
            "R-REF-4", "3004", "B1", tool.ToolId.Value, ToolType.Cm, tool.Reference, tool.Lot);

        var contextId = composition.JobOnToolStore.ContextsOf(jobOn.JobOnId.Value)
            .Single(entry => entry.ContextType == ToolContextType.Cm)
            .ContextId;

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T05TestHost.GetAsync(
            client, $"{CreatePath}?jobonId={jobOn.JobOnId.Value}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        // R1 strip: the production context is rendered from the real reads.
        Assert.Contains("data-dmo-controlo-strip=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("R-REF-4", html, StringComparison.Ordinal);
        Assert.Contains("3004", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-strip-processo=\"true\">PS", html, StringComparison.Ordinal);

        // R3 CM region: the frozen triple + live lot are rendered (lote and processo never re-entered).
        Assert.Contains("data-dmo-frozen-triple=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("5447T174", html, StringComparison.Ordinal);
        Assert.Contains("LOTE-R4", html, StringComparison.Ordinal);

        // The Peso create anchor is the real cm_id resolved through the Resumo: the operator only
        // enters the Peso-workflow facts.
        Assert.Contains($"data-dmo-anchor-cm=\"{contextId}\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-dmo-anchor-tool=\"{tool.ToolId.Value}\"", html, StringComparison.Ordinal);
    }
}