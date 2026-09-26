using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DMO.Application.Access;
using DMO.Application.Controlo.Pesos;
using DMO.Application.JobOn;
using DMO.Domain.Tools;
using DMO.IntegrationTests.JobOn;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DMO.IntegrationTests.Controlo.Pesos;

/// <summary>
/// P2-T05 transport-class (<c>I</c>) proofs of the Controlo Create endpoints: the REAL web host
/// with the REAL <c>ControloCreateEndpoints</c>/Razor pages, the REAL
/// <c>ControloCreateService</c>/<c>JobOnService</c>/<c>ToolService</c> and the REAL validators, over
/// the controlled P2-T05/P2-T04 repository doubles.
/// </summary>
/// <remarks>
/// <para>
/// Authority: P2-T05 contract §21.3 (routes 1–11), §26.3 (the published read shapes), §26.4 rows
/// JRC1–JRC4, JRC6, JRC7 (AC-R1–AC-R4, AC-R6, AC-R7) and CRE10 (AC-C6).
/// </para>
/// <para>
/// Every caller in this class holds the <c>controlo-create</c> grant the route under test requires,
/// so each observed outcome is a domain/transport outcome and never a policy denial; denials are
/// proven by <see cref="ControloCreateAccessTests"/> and covered by JRC7(g).
/// </para>
/// </remarks>
public sealed class ControloCreateEndpointsTests
{
    private const string CreatePath = "/controlo/create";

    /// <summary>
    /// JRC1 (contract §26.4) — proves AC-R1: TWO occurrences of the SAME reference (different
    /// production numbers) are BOTH returned by the productions query, and the consuming page
    /// renders the selection table region (route map + selectable table) carrying BOTH production
    /// numbers, selecting nothing automatically.
    /// </summary>
    [Fact]
    public async Task JRC1_EveryOccurrenceOfTheSameReferenceIsReturnedAndRenderedSelectable()
    {
        var composition = new P2T05TestComposition();
        var first = composition.JobOnToolStore.SeedJobOn(
            "R1-TEST", "1000", "B1", new DateOnly(2026, 1, 5));
        var second = composition.JobOnToolStore.SeedJobOn(
            "R1-TEST", "1001", "B2", new DateOnly(2026, 1, 6));

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var query = await P2T05TestHost.GetAsync(
            client, $"{CreatePath}/productions?reference=R1-TEST");
        Assert.Equal(HttpStatusCode.OK, query.StatusCode);

        var payload = await ReadJsonAsync(query);
        var productions = payload.GetProperty("productions");
        Assert.Equal(2, productions.GetArrayLength());

        var ids = productions.EnumerateArray()
            .Select(item => item.GetProperty("jobonId").GetGuid())
            .ToArray();
        Assert.Contains(first.JobOnId.Value, ids);
        Assert.Contains(second.JobOnId.Value, ids);

        var productionNumbers = productions.EnumerateArray()
            .Select(item => item.GetProperty("productionNumber").GetString())
            .ToArray();
        Assert.Contains("1000", productionNumbers);
        Assert.Contains("1001", productionNumbers);

        // The consuming Create page renders the productions table region with both rows: the
        // page-owned route map and the selectable dense table are present, nothing is selected.
        using var page = await P2T05TestHost.GetAsync(client, $"{CreatePath}?reference=R1-TEST");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("data-dmo-productions-route-map=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-dense-table=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-selection-enabled=\"true\"", html, StringComparison.Ordinal);

        // The production rows live in the selection region (the measurement-rows component uses
        // the same row marker), so the count is scoped to the productions region: from the page's
        // route map until the inputs region.
        var selectionRegion = SelectionRegion(html);
        Assert.Equal(2, Count(selectionRegion, "data-dmo-row=\"true\""));
        Assert.Equal(2, Count(html, "data-dmo-production-route="));
        Assert.Contains("1000", html, StringComparison.Ordinal);
        Assert.Contains("1001", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-selected=\"true\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("dmo-table__row--selected", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// JRC1 (contract §26.4) — proves AC-R1: a SINGLE matching occurrence still renders the
    /// selectable table with exactly one row and no auto-selection and no auto-redirect: the page
    /// never resolves the reference by itself.
    /// </summary>
    [Fact]
    public async Task JRC1_ASingleOccurrenceStillRendersASelectableTableWithNoAutoSelection()
    {
        var composition = new P2T05TestComposition();
        var only = composition.JobOnToolStore.SeedJobOn("R1-TEST", "1000", "B1");

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var page = await P2T05TestHost.GetAsync(client, $"{CreatePath}?reference=R1-TEST");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("data-dmo-productions-route-map=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-selection-enabled=\"true\"", html, StringComparison.Ordinal);

        // The single production row is counted inside the productions region only (the
        // measurement-rows component uses the same row marker outside the region).
        Assert.Equal(1, Count(SelectionRegion(html), "data-dmo-row=\"true\""));
        Assert.Equal(1, Count(html, "data-dmo-production-route="));

        // The single row is never auto-selected and no redirect/replacement happens in markup.
        Assert.DoesNotContain("aria-selected=\"true\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("dmo-table__row--selected", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http-equiv=\"refresh\"", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// JRC1 (contract §26.4) — proves AC-R1: a blank reference is a validation failure carrying
    /// <c>REFERENCE_REQUIRED</c> on the productions query and is never an empty result.
    /// </summary>
    [Fact]
    public async Task JRC1_BlankReferenceIsAValidationFailureNeverAnEmptyResult()
    {
        var composition = new P2T05TestComposition();
        composition.JobOnToolStore.SeedJobOn("R1-TEST", "1000");

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        foreach (var path in new[] { $"{CreatePath}/productions", $"{CreatePath}/productions?reference=" })
        {
            using var response = await P2T05TestHost.GetAsync(client, path);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var payload = await ReadJsonAsync(response);
            Assert.Equal("validation-failed", payload.GetProperty("reason").GetString());
            Assert.Contains(
                ControloCreateValidationErrors.ReferenceRequired,
                payload.GetProperty("errors").EnumerateArray().Select(error => error.GetString() ?? string.Empty));

            Assert.False(payload.TryGetProperty("productions", out _));
        }
    }

    /// <summary>
    /// JRC2 (contract §26.4) — proves AC-R2: with ONLY the <c>controlo-create</c> grant, the three
    /// composed P2-T04 reads (productions query, Job On ficha with the CM context, association
    /// candidates) are served 200 with the accepted shapes — one read model, no second authority.
    /// </summary>
    [Fact]
    public async Task JRC2_ComposedReadsAreServedUnderControloCreateWithTheAcceptedShapes()
    {
        var composition = new P2T05TestComposition();
        var tool = composition.SeedTool(ToolType.Cm, "5447T173", "LOTE-R2", Processo.Nnpb);
        var jobOn = composition.SeedJobOnWithCmContext(
            "R2-TEST", "1000", "B1", tool.ToolId.Value, ToolType.Cm, tool.Reference, tool.Lot);

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        // Productions query (route 2) — the accepted production-list shape.
        using (var productions = await P2T05TestHost.GetAsync(client, $"{CreatePath}/productions?reference=R2-TEST"))
        {
            Assert.Equal(HttpStatusCode.OK, productions.StatusCode);

            var payload = await ReadJsonAsync(productions);
            var items = payload.GetProperty("productions");
            Assert.Equal(1, items.GetArrayLength());

            var item = items[0];
            Assert.Equal(
                new[] { "jobonId", "machine", "productionDate", "productionNumber", "reference" },
                item.EnumerateObject().Select(member => member.Name).OrderBy(name => name, StringComparer.Ordinal));
            Assert.Equal(jobOn.JobOnId.Value, item.GetProperty("jobonId").GetGuid());
            Assert.Equal("R2-TEST", item.GetProperty("reference").GetString());
            Assert.Equal("1000", item.GetProperty("productionNumber").GetString());
            Assert.Equal("B1", item.GetProperty("machine").GetString());
        }

        // Job On ficha read (route 3) — the accepted ficha shape, JSON over the wire.
        Guid cmId;
        using (var ficha = await P2T05TestHost.GetAsync(client, $"{CreatePath}/jobons/{jobOn.JobOnId.Value}"))
        {
            Assert.Equal(HttpStatusCode.OK, ficha.StatusCode);

            var payload = await ReadJsonAsync(ficha);
            Assert.Equal(
                new[] { "contexts", "copiedFromJobOnId", "jobonId", "machine", "productionDate", "productionNumber", "reference", "version" },
                payload.EnumerateObject().Select(member => member.Name).OrderBy(name => name, StringComparer.Ordinal));
            Assert.Equal(jobOn.JobOnId.Value, payload.GetProperty("jobonId").GetGuid());
            Assert.Equal("R2-TEST", payload.GetProperty("reference").GetString());
            Assert.Equal("1000", payload.GetProperty("productionNumber").GetString());
            Assert.Equal("B1", payload.GetProperty("machine").GetString());

            var contexts = payload.GetProperty("contexts");
            Assert.Equal(1, contexts.GetArrayLength());

            var context = contexts[0];
            Assert.Equal(
                new[] { "contextId", "contextType", "tool", "toolId", "toolLot", "toolReference", "toolType" },
                context.EnumerateObject().Select(member => member.Name).OrderBy(name => name, StringComparer.Ordinal));
            Assert.Equal("CM", context.GetProperty("contextType").GetString());
            cmId = context.GetProperty("contextId").GetGuid();
            Assert.NotEqual(Guid.Empty, cmId);
            Assert.Equal(tool.ToolId.Value, context.GetProperty("toolId").GetGuid());
            Assert.Equal("CM", context.GetProperty("toolType").GetString());
            Assert.Equal("5447T173", context.GetProperty("toolReference").GetString());
            Assert.Equal("LOTE-R2", context.GetProperty("toolLot").GetString());

            var projected = context.GetProperty("tool");
            Assert.Equal("5447T173", projected.GetProperty("reference").GetString());
            Assert.Equal("NNPB", projected.GetProperty("processo").GetString());
            Assert.Contains("B1", projected.GetProperty("compatibleMachines").EnumerateArray().Select(value => value.GetString()));
        }

        // Association candidates (route 4) — the real cm_id candidates of the anchor Tool.
        using (var candidates = await P2T05TestHost.GetAsync(
                   client, $"{CreatePath}/tools/{tool.ToolId.Value}/association-candidates"))
        {
            Assert.Equal(HttpStatusCode.OK, candidates.StatusCode);

            var payload = await ReadJsonAsync(candidates);
            var items = payload.GetProperty("candidates");
            Assert.Equal(1, items.GetArrayLength());

            var item = items[0];
            Assert.Equal(
                new[] { "cmId", "jobonId", "machine", "productionNumber", "reference" },
                item.EnumerateObject().Select(member => member.Name).OrderBy(name => name, StringComparer.Ordinal));
            Assert.Equal(cmId, item.GetProperty("cmId").GetGuid());
            Assert.Equal(jobOn.JobOnId.Value, item.GetProperty("jobonId").GetGuid());
            Assert.Equal("R2-TEST", item.GetProperty("reference").GetString());
            Assert.Equal("1000", item.GetProperty("productionNumber").GetString());
            Assert.Equal("B1", item.GetProperty("machine").GetString());
        }
    }

    /// <summary>
    /// JRC3 (contract §26.4) — proves AC-R3: the missing-CM association creates the CM context
    /// through the Job On contract (the response names the REAL context identity and the frozen
    /// triple), keeps every other occurrence fact unchanged, and a type-mismatched Tool is refused
    /// with <c>TOOL_TYPE_MISMATCH</c> writing nothing.
    /// </summary>
    [Fact]
    public async Task JRC3_CmAssociationCreatesTheFrozenContextAndRefusesATypeMismatchedTool()
    {
        var composition = new P2T05TestComposition();
        var cmTool = composition.SeedTool(ToolType.Cm, "5447T173", "LOTE-R3", Processo.Nnpb);
        var mfTool = composition.SeedTool(ToolType.Mf, "5447T174", "LOTE-R3-MF");
        var jobOn = composition.JobOnToolStore.SeedJobOn("R3-TEST", "1000", "B1");

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        // Route 11 — create the missing CM context with the existing canonical CM Tool.
        Guid cmId;
        using (var created = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{CreatePath}/jobons/{jobOn.JobOnId.Value}/cm-association",
                   P2T05TestHost.Json(new
                   {
                       toolId = cmTool.ToolId.Value,
                       expectedJobOnVersion = 1,
                   })))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var payload = await ReadJsonAsync(created);
            Assert.Equal(jobOn.JobOnId.Value, payload.GetProperty("jobonId").GetGuid());
            cmId = payload.GetProperty("cmId").GetGuid();
            Assert.NotEqual(Guid.Empty, cmId);
            Assert.Equal(2, payload.GetProperty("version").GetInt32());
        }

        // The ficha now carries the CM context with the Tool's frozen triple; the other facts are
        // unchanged.
        using (var ficha = await P2T05TestHost.GetAsync(client, $"{CreatePath}/jobons/{jobOn.JobOnId.Value}"))
        {
            Assert.Equal(HttpStatusCode.OK, ficha.StatusCode);

            var payload = await ReadJsonAsync(ficha);
            Assert.Equal("R3-TEST", payload.GetProperty("reference").GetString());
            Assert.Equal("1000", payload.GetProperty("productionNumber").GetString());
            Assert.Equal("B1", payload.GetProperty("machine").GetString());
            Assert.Equal(2, payload.GetProperty("version").GetInt32());

            var context = Assert.Single(payload.GetProperty("contexts").EnumerateArray().ToArray());
            Assert.Equal("CM", context.GetProperty("contextType").GetString());
            Assert.Equal(cmId, context.GetProperty("contextId").GetGuid());
            Assert.Equal(cmTool.ToolId.Value, context.GetProperty("toolId").GetGuid());
            Assert.Equal("CM", context.GetProperty("toolType").GetString());
            Assert.Equal("5447T173", context.GetProperty("toolReference").GetString());
            Assert.Equal("LOTE-R3", context.GetProperty("toolLot").GetString());
        }

        // A type-mismatched Tool is refused BEFORE any write: 400 with TOOL_TYPE_MISMATCH and the
        // ficha stays exactly as it was.
        using (var refused = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{CreatePath}/jobons/{jobOn.JobOnId.Value}/cm-association",
                   P2T05TestHost.Json(new
                   {
                       toolId = mfTool.ToolId.Value,
                       expectedJobOnVersion = 2,
                   })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

            var payload = await ReadJsonAsync(refused);
            Assert.Equal("validation-failed", payload.GetProperty("reason").GetString());
            Assert.Contains(
                JobOnValidationErrors.ToolTypeMismatch,
                payload.GetProperty("errors").EnumerateArray().Select(error => error.GetString() ?? string.Empty));
        }

        using (var after = await P2T05TestHost.GetAsync(client, $"{CreatePath}/jobons/{jobOn.JobOnId.Value}"))
        {
            var payload = await ReadJsonAsync(after);
            Assert.Equal(2, payload.GetProperty("version").GetInt32());
            Assert.Equal("R3-TEST", payload.GetProperty("reference").GetString());
            Assert.Equal("1000", payload.GetProperty("productionNumber").GetString());
            Assert.Equal("B1", payload.GetProperty("machine").GetString());

            var context = Assert.Single(payload.GetProperty("contexts").EnumerateArray().ToArray());
            Assert.Equal(cmId, context.GetProperty("contextId").GetGuid());
            Assert.Equal(cmTool.ToolId.Value, context.GetProperty("toolId").GetGuid());
        }
    }

    /// <summary>
    /// JRC4 (contract §26.4) — proves AC-R4: a Peso created against a pending Tool anchor renders
    /// the truthful pending condition (<c>Job On por associar</c>) with NO production strip facts,
    /// and submission works while pending (the pending condition never blocks the measurement).
    /// </summary>
    [Fact]
    public async Task JRC4_PendingDraftShowsThePendingConditionAndSubmitsWhilePending()
    {
        var composition = new P2T05TestComposition();
        var tool = composition.SeedTool(ToolType.Cm, "5447T173", "LOTE-R4", Processo.Nnpb);

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        Guid pesoId;
        using (var created = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{CreatePath}/pesos",
                   PesoBody(null, tool.ToolId.Value, 20m, 2500.5m)))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var payload = await ReadJsonAsync(created);
            pesoId = payload.GetProperty("pesoId").GetGuid();
            Assert.NotEqual(Guid.Empty, pesoId);
            Assert.Equal(1, payload.GetProperty("version").GetInt32());
        }

        // The opened pending draft renders the truthful pending condition; the R1 strip carries no
        // production facts (the strip's empty state is explicit, not a fabricated production).
        using (var page = await P2T05TestHost.GetAsync(client, $"{CreatePath}?pesoId={pesoId}"))
        {
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);

            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains("Job On por associar", html, StringComparison.Ordinal);
            Assert.Contains("data-dmo-pending-condition=\"true\"", html, StringComparison.Ordinal);
            Assert.Contains("data-dmo-pending=\"true\"", html, StringComparison.Ordinal);
            Assert.Contains($"data-dmo-peso-id=\"{pesoId}\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-dmo-strip-reference=", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-dmo-strip-production=", html, StringComparison.Ordinal);
            Assert.Contains("data-dmo-strip-empty=\"true\"", html, StringComparison.Ordinal);
        }

        // Submit works while pending: the pending condition is not an error and never blocks.
        using (var submitted = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{CreatePath}/pesos/{pesoId}/submit",
                   P2T05TestHost.Json(new { expectedVersion = 1 })))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);

            var payload = await ReadJsonAsync(submitted);
            Assert.Equal(pesoId, payload.GetProperty("pesoId").GetGuid());
            Assert.Equal(2, payload.GetProperty("version").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("submittedAt").GetString()));
        }
    }

    /// <summary>
    /// JRC6 (contract §26.4) — proves AC-R6: with ONLY the <c>controlo-create</c> grant, the
    /// canonical Tool routes stay <c>ferramentas</c>-gated (403); Tool orchestration has exactly one
    /// access path and it is not Controlo Create's.
    /// </summary>
    [Fact]
    public async Task JRC6_ToolRoutesRemainFerramentasGatedForAControloCreateCaller()
    {
        var composition = new P2T05TestComposition();
        composition.SeedTool(ToolType.Cm, "5447T173", "LOTE-R6", Processo.Nnpb);

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using (var read = await P2T05TestHost.GetAsync(client, "/ferramentas/tools?reference=5447T173&limit=10"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        }

        using (var write = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   "/ferramentas/tools",
                   P2T05TestHost.Json(new
                   {
                       type = "CM",
                       reference = "9999Z",
                       lot = "LOTE-R6",
                       machines = new[] { "B1" },
                   })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        }

        // No denial wrote anything through a second path.
        Assert.Equal(1, composition.JobOnToolStore.ToolCount);
    }

    /// <summary>
    /// JRC7 (contract §26.4) — proves AC-R7: the stateless calculate with a pending Tool anchor
    /// returns the per-row results and echoes the anchor, and writes NOTHING (no id allocation, no
    /// record, no version bump).
    /// </summary>
    [Fact]
    public async Task JRC7_CalculateEchoesThePendingAnchorAndWritesNothing()
    {
        var composition = new P2T05TestComposition();
        var tool = composition.SeedTool(ToolType.Cm, "5447T173", "LOTE-R7", Processo.Nnpb);

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        var body = CalculateBody(null, tool.ToolId.Value, 20m, 2500.5m);

        using var response = await P2T05TestHost.SendJsonAsync(
            client, HttpMethod.Post, $"{CreatePath}/calculate", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await ReadJsonAsync(response);
        Assert.Equal(tool.ToolId.Value, payload.GetProperty("pendingToolId").GetGuid());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("cmId").ValueKind);
        Assert.Equal(20m, payload.GetProperty("waterTemperature").GetDecimal());
        Assert.Equal(0.9982m, payload.GetProperty("waterDensityGCm3").GetDecimal());
        Assert.Equal(2.4027m, payload.GetProperty("glassDensityGCm3").GetDecimal());

        var rows = payload.GetProperty("rows");
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal(1, rows[0].GetProperty("rowPosition").GetInt32());
        Assert.Equal(2500.5m, rows[0].GetProperty("waterWeightG").GetDecimal());
        Assert.True(rows[0].GetProperty("capacityCm3").GetDecimal() > 0);
        Assert.True(rows[0].GetProperty("glassWeightG").GetDecimal() > 0);

        // Stateless: no Peso record, no row set and no version bump exist after the calculation.
        Assert.Equal(0, composition.PesoCount);
    }

    /// <summary>
    /// JRC7 (contract §26.4) — proves AC-R7: the anchor rule is strictly XOR — no anchor is
    /// <c>PESO_ANCHOR_REQUIRED</c> and both anchors are <c>PESO_ANCHOR_CONFLICT</c>, both 400
    /// validation failures, on the stateless calculate.
    /// </summary>
    [Fact]
    public async Task JRC7_CalculateAnchorRuleIsXorWithTheExactValidationTokens()
    {
        var composition = new P2T05TestComposition();
        var tool = composition.SeedTool(ToolType.Cm, "5447T173", "LOTE-R7", Processo.Nnpb);
        var jobOn = composition.SeedJobOnWithCmContext(
            "R7-TEST", "1000", "B1", tool.ToolId.Value, ToolType.Cm, tool.Reference, tool.Lot);
        var cmId = await ReadCmContextIdAsync(client: null, composition, jobOn);

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        // (c) No anchor.
        using (var none = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{CreatePath}/calculate",
                   CalculateBody(null, null, 20m, 2500.5m)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);

            var payload = await ReadJsonAsync(none);
            Assert.Equal("validation-failed", payload.GetProperty("reason").GetString());
            Assert.Contains(
                ControloCreateValidationErrors.PesoAnchorRequired,
                payload.GetProperty("errors").EnumerateArray().Select(error => error.GetString() ?? string.Empty));
        }

        // (b) Both anchors.
        using (var both = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{CreatePath}/calculate",
                   CalculateBody(cmId, tool.ToolId.Value, 20m, 2500.5m)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);

            var payload = await ReadJsonAsync(both);
            Assert.Equal("validation-failed", payload.GetProperty("reason").GetString());
            Assert.Contains(
                ControloCreateValidationErrors.PesoAnchorConflict,
                payload.GetProperty("errors").EnumerateArray().Select(error => error.GetString() ?? string.Empty));
        }
    }

    /// <summary>
    /// JRC7 (contract §26.4) — proves AC-R7: unknown anchor identities are validation failures
    /// (<c>CM_CONTEXT_NOT_FOUND</c>/<c>TOOL_NOT_FOUND</c>, both 400), and those same missing
    /// identities are 404 ONLY on the <c>/pesos/{{id}}</c> routes — calculate NEVER 404s for anchor
    /// problems.
    /// </summary>
    [Fact]
    public async Task JRC7_UnknownAnchorsAreValidationFailuresOnlyPesoRoutesNotFound()
    {
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        var unknownCm = Guid.NewGuid();
        var unknownTool = Guid.NewGuid();

        // (d) Unknown cmId / unknown pendingToolId on the stateless calculate: 400, never 404 (e).
        using (var unknownCmResponse = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{CreatePath}/calculate",
                   CalculateBody(unknownCm, null, 20m, 2500.5m)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unknownCmResponse.StatusCode);

            var payload = await ReadJsonAsync(unknownCmResponse);
            Assert.Equal("validation-failed", payload.GetProperty("reason").GetString());
            Assert.Contains(
                ControloCreateValidationErrors.CmContextNotFound,
                payload.GetProperty("errors").EnumerateArray().Select(error => error.GetString() ?? string.Empty));
        }

        using (var unknownToolResponse = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{CreatePath}/calculate",
                   CalculateBody(null, unknownTool, 20m, 2500.5m)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unknownToolResponse.StatusCode);

            var payload = await ReadJsonAsync(unknownToolResponse);
            Assert.Equal("validation-failed", payload.GetProperty("reason").GetString());
            Assert.Contains(
                ControloCreateValidationErrors.ToolNotFound,
                payload.GetProperty("errors").EnumerateArray().Select(error => error.GetString() ?? string.Empty));
        }

        // (e) The SAME unknown identity on the record routes is a 404 — the record read is the only
        // not-found surface; the stateless calculate refuses with 400 and never 404.
        using (var getPeso = await P2T05TestHost.GetAsync(client, $"{CreatePath}/pesos/{unknownCm}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, getPeso.StatusCode);
            Assert.Equal("not-found", (await ReadJsonAsync(getPeso)).GetProperty("reason").GetString());
        }

        using (var putPeso = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{CreatePath}/pesos/{unknownCm}",
                   P2T05TestHost.Json(new
                   {
                       expectedVersion = 1,
                       waterTemperature = 20m,
                       volumeMarisaBq = (decimal?)null,
                       volumePuncaoPu = (decimal?)null,
                       previousProductionEndReference = (string?)null,
                       previousAverageWeightReference = (string?)null,
                       rows = new object[] { new { waterWeightG = 2500.5m } },
                   })))
        {
            Assert.Equal(HttpStatusCode.NotFound, putPeso.StatusCode);
        }

        using (var submit = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{CreatePath}/pesos/{unknownCm}/submit",
                   P2T05TestHost.Json(new { expectedVersion = 1 })))
        {
            Assert.Equal(HttpStatusCode.NotFound, submit.StatusCode);
        }

        using (var associate = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{CreatePath}/pesos/{unknownCm}/associate",
                   P2T05TestHost.Json(new { cmId = unknownCm, expectedVersion = 1 })))
        {
            Assert.Equal(HttpStatusCode.NotFound, associate.StatusCode);
        }
    }

    /// <summary>
    /// JRC7 (contract §26.4) — proves AC-R7: the calculate carrier and response are request-carrier
    /// facts only — the response keys are EXACTLY the six contracted members and neither the request
    /// nor the response references a <c>pesoId</c>/<c>jobonId</c> (no record identity in the
    /// stateless carrier, C3).
    /// </summary>
    [Fact]
    public async Task JRC7_CalculateCarrierAndResponseCarryOnlyTheContractedFacts()
    {
        var composition = new P2T05TestComposition();
        var tool = composition.SeedTool(ToolType.Cm, "5447T173", "LOTE-R7", Processo.Nnpb);

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        var body = CalculateBody(null, tool.ToolId.Value, 20m, 2500.5m);

        // The REQUEST carrier declares no record identity of any kind.
        Assert.DoesNotContain("pesoId", body, StringComparison.Ordinal);
        Assert.DoesNotContain("jobonId", body, StringComparison.Ordinal);

        using var response = await P2T05TestHost.SendJsonAsync(
            client, HttpMethod.Post, $"{CreatePath}/calculate", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await ReadJsonAsync(response);
        Assert.Equal(
            new[] { "cmId", "glassDensityGCm3", "pendingToolId", "rows", "waterDensityGCm3", "waterTemperature" },
            payload.EnumerateObject().Select(member => member.Name).OrderBy(name => name, StringComparer.Ordinal));

        // No record identity leaks into the response either: every response key set is the
        // contracted six and the rows carry position/facts/results only.
        var row = payload.GetProperty("rows")[0];
        Assert.Equal(
            new[] { "capacityCm3", "glassWeightG", "rowPosition", "waterWeightG" },
            row.EnumerateObject().Select(member => member.Name).OrderBy(name => name, StringComparer.Ordinal));

        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("pesoId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("jobonId", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// JRC7 (contract §26.4) — proves AC-R7: the gate — a caller holding NO <c>controlo-create</c>
    /// grant is denied the stateless calculate with 403 (full route coverage in
    /// <see cref="ControloCreateAccessTests"/>).
    /// </summary>
    [Fact]
    public async Task JRC7_CalculateIsDeniedForACallerWithoutControloCreate()
    {
        var composition = new P2T05TestComposition();
        var tool = composition.SeedTool(ToolType.Cm, "5447T173", "LOTE-R7", Processo.Nnpb);

        using var factory = P2T05TestHost.ForUser(Array.Empty<ModuleDefinition>(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T05TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            $"{CreatePath}/calculate",
            CalculateBody(null, tool.ToolId.Value, 20m, 2500.5m));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, composition.PesoCount);
    }

    /// <summary>
    /// JRC7 (contract §26.4) — proves AC-R7: no preview/persist drift — the SAME facts through
    /// <c>POST /calculate</c> and then through <c>POST /pesos</c> (same cmId anchor) persist rows
    /// whose results EQUAL the calculate preview exactly, and the published sheet carries the
    /// pending status with the anchor projections.
    /// </summary>
    [Fact]
    public async Task JRC7_PersistedRowsEqualTheCalculatePreviewWithNoDrift()
    {
        var composition = new P2T05TestComposition();
        var tool = composition.SeedTool(ToolType.Cm, "5447T173", "LOTE-R7H", Processo.Nnpb);
        var jobOn = composition.SeedJobOnWithCmContext(
            "R7H-TEST", "1000", "B1", tool.ToolId.Value, ToolType.Cm, tool.Reference, tool.Lot);

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        var cmId = await ReadCmContextIdAsync(client, composition, jobOn);

        const decimal waterTemperature = 25m;
        const decimal waterWeight = 2500.5m;

        decimal previewCapacity;
        decimal previewGlass;
        using (var calculate = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{CreatePath}/calculate",
                   CalculateBody(cmId, null, waterTemperature, waterWeight)))
        {
            Assert.Equal(HttpStatusCode.OK, calculate.StatusCode);

            var payload = await ReadJsonAsync(calculate);
            Assert.Equal(cmId, payload.GetProperty("cmId").GetGuid());
            Assert.Equal(0.9971m, payload.GetProperty("waterDensityGCm3").GetDecimal());
            Assert.Equal(2.4027m, payload.GetProperty("glassDensityGCm3").GetDecimal());

            previewCapacity = payload.GetProperty("rows")[0].GetProperty("capacityCm3").GetDecimal();
            previewGlass = payload.GetProperty("rows")[0].GetProperty("glassWeightG").GetDecimal();
            Assert.True(previewCapacity > 0);
            Assert.True(previewGlass > 0);
        }

        Guid pesoId;
        using (var created = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{CreatePath}/pesos",
                   PesoBody(cmId, null, waterTemperature, waterWeight)))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var payload = await ReadJsonAsync(created);
            pesoId = payload.GetProperty("pesoId").GetGuid();
            Assert.Equal(1, payload.GetProperty("version").GetInt32());
        }

        // The persisted row results equal the preview EXACTLY (one authoritative computation).
        using (var sheet = await P2T05TestHost.GetAsync(client, $"{CreatePath}/pesos/{pesoId}"))
        {
            Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);

            var payload = await ReadJsonAsync(sheet);
            Assert.Equal("pendente", payload.GetProperty("status").GetString());
            Assert.Equal(cmId, payload.GetProperty("cmId").GetGuid());
            Assert.Equal(JsonValueKind.Null, payload.GetProperty("toolId").ValueKind);

            var context = payload.GetProperty("context");
            Assert.Equal("5447T173", context.GetProperty("frozenToolReference").GetString());
            Assert.Equal("LOTE-R7H", context.GetProperty("frozenToolLot").GetString());

            var production = payload.GetProperty("production");
            Assert.Equal("R7H-TEST", production.GetProperty("reference").GetString());
            Assert.Equal("1000", production.GetProperty("productionNumber").GetString());
            Assert.Equal("B1", production.GetProperty("machine").GetString());

            var row = payload.GetProperty("rows")[0];
            Assert.Equal(1, row.GetProperty("rowPosition").GetInt32());
            Assert.Equal(waterWeight, row.GetProperty("waterWeightG").GetDecimal());
            Assert.Equal(previewCapacity, row.GetProperty("capacityCm3").GetDecimal());
            Assert.Equal(previewGlass, row.GetProperty("glassWeightG").GetDecimal());
        }
    }

    /// <summary>
    /// CRE10 (contract §26.4) — proves AC-C6: the route scan of the REAL routing table — no DELETE
    /// route exists under <c>/controlo/create/pesos</c> or
    /// <c>/controlo/create/definicoes/repairers</c>, no route pattern ends in <c>/delete</c>, and
    /// <c>/controlo/create/calculate</c> exists with POST only (no PUT/DELETE).
    /// </summary>
    [Fact]
    public void CRE10_NoDeleteRoutesExistAndCalculateIsPostOnly()
    {
        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), new P2T05TestComposition());
        var endpoints = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .ToArray();

        Assert.NotEmpty(endpoints);

        foreach (var endpoint in endpoints)
        {
            var path = NormalizePath(endpoint.RoutePattern.RawText ?? string.Empty);

            // The no-delete rules are P2-T05-scoped: the pre-existing administration surface owns
            // its own (non-P2-T05) delete-template route.
            if (!path.StartsWith("/controlo/create", StringComparison.Ordinal))
            {
                continue;
            }

            var verbs = VerbsOf(endpoint);

            if (path.StartsWith("/controlo/create/pesos", StringComparison.Ordinal))
            {
                Assert.DoesNotContain(HttpMethods.Delete, verbs);
            }

            if (path.StartsWith("/controlo/create/definicoes/repairers", StringComparison.Ordinal))
            {
                Assert.DoesNotContain(HttpMethods.Delete, verbs);
            }

            Assert.False(
                path.EndsWith("/delete", StringComparison.Ordinal),
                $"A route ending in /delete exists: {path}.");
        }

        var calculate = endpoints.Single(endpoint =>
            NormalizePath(endpoint.RoutePattern.RawText ?? string.Empty) == "/controlo/create/calculate");

        Assert.Equal(new[] { HttpMethods.Post }, VerbsOf(calculate));
    }

    // ---- arrangement helpers -------------------------------------------------------------

    /// <summary>Builds the route-8 calculate carrier (XOR anchor + the input facts).</summary>
    private static string CalculateBody(
        Guid? cmId,
        Guid? pendingToolId,
        decimal temperature,
        params decimal[] waterWeights) =>
        P2T05TestHost.Json(new
        {
            cmId,
            pendingToolId,
            waterTemperature = temperature,
            volumeMarisaBq = (decimal?)null,
            volumePuncaoPu = (decimal?)null,
            previousProductionEndReference = (string?)null,
            previousAverageWeightReference = (string?)null,
            rows = waterWeights.Select(weight => new { waterWeightG = weight }).ToArray(),
        });

    /// <summary>Builds the route-5 create carrier (XOR anchor + the input facts).</summary>
    private static string PesoBody(
        Guid? cmId,
        Guid? pendingToolId,
        decimal temperature,
        params decimal[] waterWeights) =>
        P2T05TestHost.Json(new
        {
            cmId,
            pendingToolId,
            waterTemperature = temperature,
            volumeMarisaBq = (decimal?)null,
            volumePuncaoPu = (decimal?)null,
            previousProductionEndReference = (string?)null,
            previousAverageWeightReference = (string?)null,
            rows = waterWeights.Select(weight => new { waterWeightG = weight }).ToArray(),
        });

    /// <summary>
    /// Reads the CM context identity of a seeded occurrence through the accepted ficha read (HTTP),
    /// or through the store's own repository read when no client is available yet.
    /// </summary>
    private static async Task<Guid> ReadCmContextIdAsync(
        HttpClient? client,
        P2T05TestComposition composition,
        DMO.Domain.JobOn.JobOn jobOn)
    {
        if (client is not null)
        {
            using var ficha = await P2T05TestHost.GetAsync(client, $"{CreatePath}/jobons/{jobOn.JobOnId.Value}");
            Assert.Equal(HttpStatusCode.OK, ficha.StatusCode);

            var payload = await ReadJsonAsync(ficha);
            return payload.GetProperty("contexts")[0].GetProperty("contextId").GetGuid();
        }

        var repository = (DMO.Application.Repositories.IJobOnRepository)composition.JobOnToolStore;
        var persisted = await repository.GetByIdAsync(jobOn.JobOnId.Value, CancellationToken.None);
        Assert.NotNull(persisted);

        var context = Assert.Single(persisted!.Contexts);
        Assert.Equal(ToolContextType.Cm, context.ContextType);

        return context.ContextId;
    }

    /// <summary>Reads a JSON response body into a detached element.</summary>
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }

    /// <summary>Counts non-overlapping occurrences of a fragment in text.</summary>
    private static int Count(string text, string fragment)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }

    /// <summary>
    /// The R2 selection region of the rendered Create page: from the productions route map until
    /// the inputs region. The measurement-rows component uses the SAME row marker, so the
    /// productions row count is asserted within this region only.
    /// </summary>
    private static string SelectionRegion(string html)
    {
        var start = html.IndexOf("data-dmo-productions-route-map=\"true\"", StringComparison.Ordinal);
        var end = html.IndexOf("data-dmo-controlo-region=\"inputs\"", StringComparison.Ordinal);

        Assert.True(
            start >= 0 && end > start,
            "The rendered page must carry the productions route map before the inputs region.");

        return html.Substring(start, end - start);
    }

    /// <summary>The normalized path template of an endpoint (parameters collapsed to <c>{}</c>,
    /// leading slash enforced), matching the accepted P2-T04 inventory helper.</summary>
    private static string NormalizePath(string rawText)
    {
        var normalized = Regex.Replace(rawText, "\\{[^}]*\\}", "{}");

        if (normalized.Length > 0 && normalized[0] != '/')
        {
            normalized = '/' + normalized;
        }

        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

    /// <summary>The HTTP verbs an endpoint answers (a Razor Page endpoint declares none: GET).</summary>
    private static IReadOnlyList<string> VerbsOf(RouteEndpoint endpoint)
    {
        var metadata = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>();
        if (metadata is null)
        {
            return [HttpMethods.Get];
        }

        return metadata.HttpMethods
            .Where(method => !string.Equals(method, HttpMethods.Head, StringComparison.Ordinal))
            .ToArray();
    }
}