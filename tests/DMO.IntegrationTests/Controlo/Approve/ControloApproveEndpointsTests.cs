using System.Net;
using System.Text.Json;
using DMO.Application.Access;
using DMO.IntegrationTests.Controlo.Pesos;

namespace DMO.IntegrationTests.Controlo.Approve;

/// <summary>
/// P2-T06 integration â€” the HTTP-class rows (contract Â§26.4): pending/history list behavior over
/// the REAL services (PL1/PL2/PL3/H2/H5), exact-record review consistency with the Create read
/// (R4), the absent-comparison region (CP3) and the decision flows over HTTP (same-<c>peso_id</c>,
/// reason requirements, refusals, reopen restoring the closed Create routes).
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract Â§12/Â§13/Â§15/Â§19 and Â§26.4 rows PL2/PL3/H2/H5/R4/CP3 (plus the I-side
/// of the J/O/K rows proved at DB level in <c>PesoReviewRepositoryIntegrationTests</c>).</remarks>
public sealed class ControloApproveEndpointsTests
{
    // ----------------------------------------------------------------------------------------
    // Fixture helpers
    // ----------------------------------------------------------------------------------------

    private static async Task<Guid> SeedSubmittedPesoAsync(
        P2T06TestComposition composition,
        string referenceToken)
    {
        var tool = composition.SeedTool(DMO.Domain.Tools.ToolType.Cm, $"ref-{referenceToken}", "01", DMO.Domain.Tools.Processo.Nnpb, "B1");
        var jobOn = composition.SeedJobOnWithCmContext(
            $"ref-{referenceToken}", $"pn-{referenceToken}", "B1", tool.ToolId.Value, DMO.Domain.Tools.ToolType.Cm, tool.Reference, tool.Lot);

        // The CM context id is produced by the Job On seeding; resolve it through the traversal.
        return await SeedSubmittedPesoAsync(composition, jobOn, referenceToken);
    }

    private static async Task<Guid> SeedSubmittedPesoAsync(
        P2T06TestComposition composition,
        DMO.Domain.JobOn.JobOn jobOn,
        string referenceToken)
    {
        // The seeded Job On carries exactly one CM context (the accepted arrangement); that real
        // cm_id is the Peso's production anchor.
        var cm = jobOn.Contexts.First(context => context.ContextType == DMO.Domain.Tools.ToolContextType.Cm);

        return await composition.SeedReviewablePesoAsync(cmId: cm.ContextId);
    }

    // ----------------------------------------------------------------------------------------
    // PL2 (AC-AP1/AC-AP3): the pending filters are backend-applied; invalid input is refused
    // ----------------------------------------------------------------------------------------

    [Fact]
    public async Task PL2_PendingFiltersAreBackendAppliedAndInvalidPagingIsRefused()
    {
        var composition = new P2T06TestComposition();
        var pesoId = await SeedSubmittedPesoAsync(composition, "PL2A");

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        // The whole reviewable set.
        using (var all = await P2T06TestHost.GetAsync(client, "/controlo/approve/pending"))
        {
            Assert.Equal(HttpStatusCode.OK, all.StatusCode);
            var payload = await ReadJsonAsync(all);
            var rows = payload.GetProperty("rows");
            Assert.Contains(rows.EnumerateArray(), row => row.GetProperty("pesoId").GetGuid() == pesoId);
        }

        // A matching reference filter still returns the row (backend-applied through cm_id).
        using (var filtered = await P2T06TestHost.GetAsync(
                   client,
                   "/controlo/approve/pending?reference=ref-PL2A"))
        {
            Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
            var rows = (await ReadJsonAsync(filtered)).GetProperty("rows");
            Assert.Single(rows.EnumerateArray());
        }

        // A non-matching filter returns an explicit EMPTY, never a fallback list.
        using (var empty = await P2T06TestHost.GetAsync(
                   client,
                   "/controlo/approve/pending?reference=ref-NADA"))
        {
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            Assert.Empty((await ReadJsonAsync(empty)).GetProperty("rows").EnumerateArray());
        }

        // Ill-formed paging is a typed 400 FILTER_INVALID, never a silent full list.
        using (var invalid = await P2T06TestHost.GetAsync(client, "/controlo/approve/pending?page=0"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var payload = await ReadJsonAsync(invalid);
            Assert.Equal("validation-failed", payload.GetProperty("reason").GetString());
            Assert.Contains(payload.GetProperty("errors").EnumerateArray(),
                error => error.GetString() == "FILTER_INVALID");
        }
    }

    /// <summary>PL1 (AC-AP1): the pending list returns exactly the reviewable set.</summary>
    [Fact]
    public async Task PL1_PendingListContainsExactlyTheReviewableSet()
    {
        var composition = new P2T06TestComposition();
        var reviewable = await SeedSubmittedPesoAsync(composition, "PL1A");
        var draftTool = composition.SeedTool(DMO.Domain.Tools.ToolType.Cm, "ref-PL1B", "01", DMO.Domain.Tools.Processo.Nnpb, "B1");
        var draftPesoId = await SeedDraftAsync(composition, draftTool.ToolId.Value);
        var rejected = await SeedSubmittedPesoAsync(composition, "PL1C");
        var rejectedPeso = (await composition.Pesos.GetByIdAsync(rejected, CancellationToken.None))!;
        await composition.Review.DecisionAsync(
            new DMO.Domain.Controlo.PesoReviewDecision(
                DMO.Domain.Controlo.PesoReviewDecisionId.New(),
                DMO.Domain.Controlo.PesoId.From(rejected),
                DMO.Domain.Controlo.PesoReviewDecisionKind.NaoAprovado,
                P2T06TestHost.ActorUserId,
                DateTimeOffset.UtcNow,
                "Motivo de rejeição",
                DMO.Domain.Controlo.PesoStatus.Pendente,
                rejectedPeso.Version,
                DateTimeOffset.UtcNow),
            expectedPesoVersion: rejectedPeso.Version,
            CancellationToken.None);

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T06TestHost.GetAsync(client, "/controlo/approve/pending");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ids = (await ReadJsonAsync(response))
            .GetProperty("rows")
            .EnumerateArray()
            .Select(row => row.GetProperty("pesoId").GetGuid())
            .ToList();

        Assert.Contains(reviewable, ids);
        Assert.DoesNotContain(draftPesoId, ids);    // drafts never appear
        Assert.DoesNotContain(rejected, ids);       // decided records never appear
    }

    // ----------------------------------------------------------------------------------------
    // PL3 (AC-AP2): opening a pending item loads the EXACT submitted peso_id
    // ----------------------------------------------------------------------------------------

    [Fact]
    public async Task PL3_OpeningAPendingItemLoadsTheExactSubmittedRecord()
    {
        var composition = new P2T06TestComposition();
        var pesoId = await SeedSubmittedPesoAsync(composition, "PL3A");

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T06TestHost.GetAsync(client, $"/controlo/approve/pesos/{pesoId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sheet = (await ReadJsonAsync(response)).GetProperty("peso");
        Assert.Equal(pesoId, sheet.GetProperty("pesoId").GetGuid());
        Assert.Equal("pendente", sheet.GetProperty("status").GetString());
        
        Assert.Equal(2.4027m, sheet.GetProperty("glassDensityGCm3").GetDecimal());
    }

    // ----------------------------------------------------------------------------------------
    // R4 (AC-R4/AC-RD1): the approval review read returns exactly the persisted facts â€” the same
    // values as the Create-side read (no recalculation observable in the response)
    // ----------------------------------------------------------------------------------------

    [Fact]
    public async Task R4_TheApprovalReviewReadMatchesTheCreateSideReadExactly()
    {
        var composition = new P2T06TestComposition();
        var pesoId = await SeedSubmittedPesoAsync(composition, "R4A");

        // The created fixture went through the store's OWN repository path; read it back with the
        // real shared service shape through the HTTP review route and compare the facts 1:1 with
        // the store's Peso (the same facts the Create GET would report).
        var stored = await composition.Pesos.GetByIdAsync(pesoId, CancellationToken.None);

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T06TestHost.GetAsync(client, $"/controlo/approve/pesos/{pesoId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sheet = (await ReadJsonAsync(response)).GetProperty("peso");

        Assert.Equal(stored!.WaterTemperature, sheet.GetProperty("waterTemperature").GetDecimal());
        Assert.Equal(stored.GlassDensityGCm3, sheet.GetProperty("glassDensityGCm3").GetDecimal());
        Assert.Equal(stored.Version, sheet.GetProperty("version").GetInt32());
        Assert.Equal(P2T06TestHost.ActorUserId, sheet.GetProperty("submittedByUserId").GetGuid());

        var rows = sheet.GetProperty("rows").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal(stored.Rows[0].CapacityCm3, rows[0].GetProperty("capacityCm3").GetDecimal());
        Assert.Equal(stored.Rows[0].GlassWeightG, rows[0].GetProperty("glassWeightG").GetDecimal());
    }

    // ----------------------------------------------------------------------------------------
    // CP3 (AC-CP2/AC-CP4): with the current schema the review sheet carries NO comparison region
    // and fabricates none â€” no previous_peso_id anywhere
    // ----------------------------------------------------------------------------------------

    [Fact]
    public async Task CP3_TheReviewSheetCarriesNoComparisonRegionAndNoPreviousPesoRelation()
    {
        var composition = new P2T06TestComposition();
        var pesoId = await SeedSubmittedPesoAsync(composition, "CP3A");

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        var raw = await (await P2T06TestHost.GetAsync(client, $"/controlo/approve/pesos/{pesoId}"))
            .Content.ReadAsStringAsync();

        Assert.DoesNotContain("previousPeso", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("comparison", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("latest", raw, StringComparison.OrdinalIgnoreCase);
    }

    // ----------------------------------------------------------------------------------------
    // Decision flows over HTTP (J1/O1/K1/K3/D1/D4 â€” I-side; DB proofs in the repository rows)
    // ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Approve_TurnsTheSamePesoIdAprovadoWithBackendActor()
    {
        var composition = new P2T06TestComposition();
        var pesoId = await SeedSubmittedPesoAsync(composition, "APP1");

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T06TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/controlo/approve/pesos/{pesoId}/approve",
            P2T06TestHost.Json(new { expectedVersion = 2 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await ReadJsonAsync(response);
        Assert.Equal("aprovado", payload.GetProperty("decision").GetString());
        Assert.Equal(pesoId, payload.GetProperty("pesoId").GetGuid());

        // The SAME row is now approved: the decision trail shows the backend actor.
        var decision = Assert.Single(composition.Review.AllDecisions);
        Assert.Equal(P2T06TestHost.ActorUserId, decision.DecidedByUserId);
        Assert.Equal(DMO.Domain.Controlo.PesoReviewDecisionKind.Aprovado, decision.Decision);
        Assert.Equal(pesoId, decision.PesoId.Value);
    }

    [Fact]
    public async Task Reject_RequiresReasonAndDoesNotWriteAnythingOnBlankReason()
    {
        var composition = new P2T06TestComposition();
        var pesoId = await SeedSubmittedPesoAsync(composition, "REJ1");

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        // Blank reason â†’ 400 REJECT_REASON_REQUIRED, nothing written.
        using (var blank = await P2T06TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"/controlo/approve/pesos/{pesoId}/reject",
                   P2T06TestHost.Json(new { expectedVersion = 2, reason = "   " })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
            Assert.Contains(
                (await ReadJsonAsync(blank)).GetProperty("errors").EnumerateArray(),
                error => error.GetString() == "REJECT_REASON_REQUIRED");
            Assert.Empty(composition.Review.AllDecisions);
        }

        // Valid reject â†’ same peso_id nao_aprovado with the reason recorded.
        using (var rejected = await P2T06TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"/controlo/approve/pesos/{pesoId}/reject",
                   P2T06TestHost.Json(new { expectedVersion = 2, reason = "Leitura fora do esperado." })))
        {
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
            var decision = Assert.Single(composition.Review.AllDecisions);
            Assert.Equal(DMO.Domain.Controlo.PesoReviewDecisionKind.NaoAprovado, decision.Decision);
            Assert.Equal("Leitura fora do esperado.", decision.Reason);
            Assert.Equal(pesoId, decision.PesoId.Value);
        }
    }

    [Fact]
    public async Task K1_StaleVersionReturns409WithNothingWritten()
    {
        var composition = new P2T06TestComposition();
        var pesoId = await SeedSubmittedPesoAsync(composition, "STL1");

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T06TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/controlo/approve/pesos/{pesoId}/approve",
            P2T06TestHost.Json(new { expectedVersion = 99 }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("stale-version", (await ReadJsonAsync(response)).GetProperty("reason").GetString());
        Assert.Empty(composition.Review.AllDecisions);
    }

    [Fact]
    public async Task K3_DoubleDecisionsAreRefusedWithAlreadyDecided()
    {
        var composition = new P2T06TestComposition();
        var pesoId = await SeedSubmittedPesoAsync(composition, "DUB1");

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using (var first = await P2T06TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"/controlo/approve/pesos/{pesoId}/approve",
                   P2T06TestHost.Json(new { expectedVersion = 2 })))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using (var second = await P2T06TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"/controlo/approve/pesos/{pesoId}/approve",
                   P2T06TestHost.Json(new { expectedVersion = 3 })))
        {
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("already-decided", (await ReadJsonAsync(second)).GetProperty("reason").GetString());
        }

        Assert.Single(composition.Review.AllDecisions);
    }

    /// <summary>
    /// O1 (AC-O1): reopen returns the SAME <c>peso_id</c> to <c>pendente</c> with the submitted
    /// handoff cleared â€” the CLOSED P2-T05 edit/submit routes then work again (proven through the
    /// real shared service over the same store).
    /// </summary>
    [Fact]
    public async Task O1_ReopenRestoresTheDraftEditableHandoffForTheClosedCreateRoutes()
    {
        var composition = new P2T06TestComposition();
        var pesoId = await SeedSubmittedPesoAsync(composition, "REO1");

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var reopened = await P2T06TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/controlo/approve/pesos/{pesoId}/reopen",
            P2T06TestHost.Json(new { expectedVersion = 2, reason = "Correção da temperatura submetida." }));

        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        Assert.Equal("reaberto", (await ReadJsonAsync(reopened)).GetProperty("decision").GetString());

        // The stored SAME row is back to pendente with the handoff cleared (Create editability).
        var stored = await composition.Pesos.GetByIdAsync(pesoId, CancellationToken.None);
        Assert.Equal(DMO.Domain.Controlo.PesoStatus.Pendente, stored!.Status);
        Assert.Null(stored.SubmittedAt);
        Assert.Null(stored.SubmittedByUserId);
        Assert.Equal(pesoId, stored.PesoId.Value); // SAME identity â€” no clone, no replacement

        // The trail preserved the reopen event with its reason.
        var event_ = Assert.Single(composition.Review.AllDecisions);
        Assert.Equal(DMO.Domain.Controlo.PesoReviewDecisionKind.Reaberto, event_.Decision);
        Assert.Equal("Correção da temperatura submetida.", event_.Reason);
    }

    // ----------------------------------------------------------------------------------------
    // H2/H5 (AC-H2/AC-H5): history filters are backend-applied; rows carry the exact peso_id and
    // the decision facts; opening a row loads the exact record including its trail
    // ----------------------------------------------------------------------------------------

    [Fact]
    public async Task H2_HistoryFiltersAreBackendAppliedAndInvalidInputIsRefused()
    {
        var composition = new P2T06TestComposition();
        var pesoId = await SeedSubmittedPesoAsync(composition, "HIS2A");

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using (var all = await P2T06TestHost.GetAsync(client, "/controlo/approve/history"))
        {
            Assert.Equal(HttpStatusCode.OK, all.StatusCode);
            Assert.Contains((await ReadJsonAsync(all)).GetProperty("rows").EnumerateArray(),
                row => row.GetProperty("pesoId").GetGuid() == pesoId);
        }

        using (var stateFiltered = await P2T06TestHost.GetAsync(
                   client,
                   "/controlo/approve/history?reviewState=aprovado"))
        {
            Assert.Equal(HttpStatusCode.OK, stateFiltered.StatusCode);
            Assert.Empty((await ReadJsonAsync(stateFiltered)).GetProperty("rows").EnumerateArray());
        }

        using (var invalid = await P2T06TestHost.GetAsync(
                   client,
                   "/controlo/approve/history?reviewState=bloqueado"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("validation-failed", (await ReadJsonAsync(invalid)).GetProperty("reason").GetString());
        }
    }

    [Fact]
    public async Task H5_HistoryRowsCarryTheExactPesoFactsAndOpenLoadsTheExactRecordWithItsTrail()
    {
        var composition = new P2T06TestComposition();
        var pesoId = await SeedSubmittedPesoAsync(composition, "HIS5A");

        // Decide once so the history row carries a last-decision summary.
        await composition.Review.DecisionAsync(
            new DMO.Domain.Controlo.PesoReviewDecision(
                DMO.Domain.Controlo.PesoReviewDecisionId.New(),
                DMO.Domain.Controlo.PesoId.From(pesoId),
                DMO.Domain.Controlo.PesoReviewDecisionKind.Aprovado,
                P2T06TestHost.ActorUserId,
                DateTimeOffset.UtcNow,
                null,
                DMO.Domain.Controlo.PesoStatus.Pendente,
                2,
                DateTimeOffset.UtcNow),
            expectedPesoVersion: 2,
            CancellationToken.None);

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using (var history = await P2T06TestHost.GetAsync(client, "/controlo/approve/history"))
        {
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);
            var row = Assert.Single((await ReadJsonAsync(history)).GetProperty("rows").EnumerateArray());
            Assert.Equal(pesoId, row.GetProperty("pesoId").GetGuid());
            Assert.Equal("aprovado", row.GetProperty("status").GetString());
            Assert.Equal(1, row.GetProperty("decisionCount").GetInt32());
            Assert.Equal("aprovado", row.GetProperty("lastDecision").GetProperty("decision").GetString());
        }

        // The trail endpoint returns the complete decision trail (decision/actor/time/version).
        using (var decisions = await P2T06TestHost.GetAsync(
                   client,
                   $"/controlo/approve/pesos/{pesoId}/decisions"))
        {
            Assert.Equal(HttpStatusCode.OK, decisions.StatusCode);
            var item = Assert.Single((await ReadJsonAsync(decisions)).GetProperty("decisions").EnumerateArray());
            Assert.Equal("aprovado", item.GetProperty("decision").GetString());
            Assert.Equal(P2T06TestHost.ActorUserId, item.GetProperty("decidedByUserId").GetGuid());
            Assert.Equal(2, item.GetProperty("pesoVersionAtDecision").GetInt32());
        }
    }

    /// <summary>D5 (AC-D1/K3): decisions on a draft are refused with <c>not-reviewable</c>.</summary>
    [Fact]
    public async Task D5_ApproveOnADraftReturns409NotReviewableAndWritesNothing()
    {
        var composition = new P2T06TestComposition();
        var tool = composition.SeedTool(DMO.Domain.Tools.ToolType.Cm, "ref-D5A", "01", DMO.Domain.Tools.Processo.Nnpb, "B1");
        var plainDraft = await SeedDraftAsync(composition, tool.ToolId.Value);

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T06TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/controlo/approve/pesos/{plainDraft}/approve",
            P2T06TestHost.Json(new { expectedVersion = 1 }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("not-reviewable", (await ReadJsonAsync(response)).GetProperty("reason").GetString());
        Assert.Empty(composition.Review.AllDecisions);
    }

    private static async Task<Guid> SeedDraftAsync(P2T06TestComposition composition, Guid toolId)
    {
        var now = DateTimeOffset.UtcNow;
        var pesoId = DMO.Domain.Controlo.PesoId.New();

        var peso = new DMO.Domain.Controlo.Peso(
            pesoId,
            CmId: null,
            toolId,
            DMO.Domain.Controlo.PesoStatus.Pendente,
            SubmittedAt: null,
            SubmittedByUserId: null,
            WaterTemperature: 25m,
            VolumeMarisaBq: null,
            VolumePuncaoPu: null,
            GlassDensityGCm3: 2.4027m,
            PreviousProductionEndReference: null,
            PreviousAverageWeightReference: null,
            Version: 1,
            CreatedByUserId: Guid.NewGuid(),
            CreatedAt: now,
            UpdatedAt: now,
            Rows:
            [
                new DMO.Domain.Controlo.PesoMeasurementRow(
                    DMO.Domain.Controlo.PesoMeasurementRowId.New(),
                    pesoId,
                    1,
                    997.1m,
                    1000m,
                    2402.7000m,
                    now),
            ]);

        var created = await composition.Pesos.CreatedAsync(peso, peso.Rows, CancellationToken.None);
        return created.PesoId.Value;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}