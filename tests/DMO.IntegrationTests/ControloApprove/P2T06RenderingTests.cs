using System.Net;
using DMO.Application.Access;
using DMO.Domain.Tools;
using DMO.IntegrationTests.ControloCreate;
using DMO.IntegrationTests.JobOn;

namespace DMO.IntegrationTests.ControloApprove;

/// <summary>
/// P2-T06 rendered-surface (<c>R</c>) proofs: the REAL Razor surfaces of the Controlo Approve
/// area, rendered by the REAL host over the controlled store, at the canonical fixed-desktop
/// composition â€” rows PL4/H3/L2/RD2/K5 (markup part) of contract Â§26.4.
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract Â§20/Â§21 (regions + fixed desktop), Â§16 (renderer parity RD2) and
/// Â§26.4.</remarks>
public sealed class P2T06RenderingTests
{
    private const string ApprovePath = "/controlo/approve";
    private const string HistoricoPath = "/controlo/approve/historico";

    private static async Task<Guid> SeedSubmittedPesoAsync(
        P2T06TestComposition composition,
        string referenceToken,
        int rowCount = 2)
    {
        var tool = composition.SeedTool(ToolType.Cm, $"ref-{referenceToken}", "01", Processo.Nnpb, "B1");
        var jobOn = composition.SeedJobOnWithCmContext(
            $"ref-{referenceToken}", $"pn-{referenceToken}", "B1", tool.ToolId.Value, ToolType.Cm, tool.Reference, tool.Lot);
        var cm = jobOn.Contexts.First(context => context.ContextType == ToolContextType.Cm);

        return await SeedSubmittedPesoWithRowsAsync(composition, cm.ContextId, rowCount);
    }

    private static async Task<Guid> SeedSubmittedPesoWithRowsAsync(
        P2T06TestComposition composition,
        Guid cmId,
        int rowCount)
    {
        var now = DateTimeOffset.UtcNow;
        var pesoId = DMO.Domain.Controlo.PesoId.New();

        var rows = Enumerable.Range(1, rowCount)
            .Select(position => new DMO.Domain.Controlo.PesoMeasurementRow(
                DMO.Domain.Controlo.PesoMeasurementRowId.New(),
                pesoId,
                position,
                997.1m * position,
                1000m * position,
                2402.7000m * position,
                now))
            .ToList();

        var peso = new DMO.Domain.Controlo.Peso(
            pesoId,
            cmId,
            ToolId: null,
            DMO.Domain.Controlo.PesoStatus.Pendente,
            SubmittedAt: null,
            SubmittedByUserId: null,
            WaterTemperature: 20.5m,
            VolumeMarisaBq: 1.25m,
            VolumePuncaoPu: 0.75m,
            GlassDensityGCm3: 2.4027m,
            PreviousProductionEndReference: "SAP-END",
            PreviousAverageWeightReference: "SAP-WEIGHT",
            Version: 1,
            CreatedByUserId: Guid.NewGuid(),
            CreatedAt: now,
            UpdatedAt: now,
            Rows: rows);

        var created = await composition.Pesos.CreatedAsync(peso, peso.Rows, CancellationToken.None);
        var submitted = created with { SubmittedAt = now.AddMinutes(-5), SubmittedByUserId = P2T06TestHost.ActorUserId };
        await composition.Pesos.SubmittedAsync(submitted, CancellationToken.None);

        return created.PesoId.Value;
    }

    /// <summary>
    /// PL4 (AC-AP2/AC-H3) â€” the Aprovar page renders the pending table with the selection/open
    /// arbitration (single click selects, double click opens): the table region carries the
    /// selection/open flags, every row is keyed by the exact <c>peso_id</c> and NO per-row action
    /// grid exists â€” actions live outside the table (in the decision region).
    /// </summary>
    [Fact]
    public async Task PL4_TheAprovarPageUsesSelectionAndOpenWithoutPerRowActionGrids()
    {
        var composition = new P2T06TestComposition();
        await SeedSubmittedPesoAsync(composition, "PL4A");

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T06TestHost.GetAsync(client, ApprovePath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-dmo-dense-table=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-selection-enabled=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-open-enabled=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-row-key=", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-open-route-map=\"true\"", html, StringComparison.Ordinal);

        // No per-row action button grid: no row-scoped action of this surface exists.
        Assert.DoesNotContain("data-dmo-action-scope=\"row\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// H3 (AC-H3) â€” the Histórico page renders the table with selection/open semantics and the
    /// detail (opened record) renders the decision BAR outside the table â€” no per-row action grid.
    /// </summary>
    [Fact]
    public async Task H3_TheHistoricoPageUsesSelectionAndOpenWithOutsideTableActions()
    {
        var composition = new P2T06TestComposition();
        var pesoId = await SeedSubmittedPesoAsync(composition, "H3A");

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var list = await P2T06TestHost.GetAsync(client, HistoricoPath);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listHtml = await list.Content.ReadAsStringAsync();

        Assert.Contains("data-dmo-dense-table=\"true\"", listHtml, StringComparison.Ordinal);
        Assert.Contains("data-dmo-selection-enabled=\"true\"", listHtml, StringComparison.Ordinal);
        Assert.Contains("data-dmo-open-enabled=\"true\"", listHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("data-dmo-action-scope=\"row\"", listHtml, StringComparison.Ordinal);

        using var detail = await P2T06TestHost.GetAsync(
            client,
            $"{HistoricoPath}?pesoId={pesoId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailHtml = await detail.Content.ReadAsStringAsync();

        Assert.Contains("data-dmo-decision-bar=\"true\"", detailHtml, StringComparison.Ordinal);
        Assert.Contains($"data-dmo-peso-id=\"{pesoId}\"", detailHtml, StringComparison.Ordinal);
        Assert.Contains("data-dmo-reason-region=\"true\"", detailHtml, StringComparison.Ordinal);
    }

    /// <summary>
    /// L2 (AC-L1/AC-L2) â€” Aprovar and Histórico render the canonical fixed-desktop region-stable
    /// composition: the five (H: three) regions in their structural order with the linked
    /// breakpoint-free stylesheet.
    /// </summary>
    [Fact]
    public async Task L2_BothPagesRenderTheRegionStableFixedDesktopComposition()
    {
        var composition = new P2T06TestComposition();
        var pesoId = await SeedSubmittedPesoAsync(composition, "L2A");

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var approve = await P2T06TestHost.GetAsync(client, $"{ApprovePath}?pesoId={pesoId}");
        var approveHtml = await approve.Content.ReadAsStringAsync();

        AssertInOrder(approveHtml,
            "data-dmo-approve-region=\"filters\"",
            "data-dmo-approve-region=\"pending\"",
            "data-dmo-approve-region=\"review\"",
            "data-dmo-approve-region=\"actions\"",
            "data-dmo-approve-region=\"trail\"");

        Assert.Contains("dmo-controlo-approve.css", approveHtml, StringComparison.Ordinal);

        using var historico = await P2T06TestHost.GetAsync(client, $"{HistoricoPath}?pesoId={pesoId}");
        var historicoHtml = await historico.Content.ReadAsStringAsync();

        AssertInOrder(historicoHtml,
            "data-dmo-approve-region=\"filters\"",
            "data-dmo-approve-region=\"history\"",
            "data-dmo-approve-region=\"detail\"");

        // The linked stylesheet is breakpoint-free (LAY1 covers the full rule; this row proves the
        // rendered page actually links it).
        var css = P2T04ProductionScan.WithoutCssComments(
            P2T04ProductionScan.Read("src/DMO.Web/wwwroot/css/dmo-controlo-approve.css"));
        Assert.False(string.IsNullOrWhiteSpace(css), "The P2-T06 stylesheet must carry real rules.");
        Assert.DoesNotContain("@media", css, StringComparison.Ordinal);
        Assert.DoesNotContain("@container", css, StringComparison.Ordinal);
        Assert.DoesNotContain("@supports", css, StringComparison.Ordinal);
    }

    /// <summary>
    /// K5 (AC-K4, markup part) â€” the decision region renders the page-owned conflict surface that
    /// the adapter fills with the accepted conflict presentation + explicit reload recovery; the
    /// surface is present for every opened record (the behavioral half is the node harness of
    /// <see cref="DmoControloApproveAdapterBehaviorTests"/>).
    /// </summary>
    [Fact]
    public async Task K5_TheDecisionRegionCarriesThePageOwnedConflictSurface()
    {
        var composition = new P2T06TestComposition();
        var pesoId = await SeedSubmittedPesoAsync(composition, "K5A");

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T06TestHost.GetAsync(client, $"{ApprovePath}?pesoId={pesoId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-dmo-approve-state=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-version=\"2\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// RD2 (AC-RD2) â€” renderer parity: the SAME fixture rendered by the Create read-only
    /// (submitted) view and the Approve review view shows the same operational facts with the same
    /// labels, order and â‰¤ 2-dp normalization â€” mechanically compared over the shared
    /// <c>data-dmo-*</c> hooks; no field is hidden in review that Create shows.
    /// </summary>
    [Fact]
    public async Task RD2_TheReviewRenderingIsFieldForFieldParityWithTheCreateReadOnlyView()
    {
        var composition = new P2T06TestComposition();
        var tool = composition.SeedTool(ToolType.Cm, "ref-RD2A", "01", Processo.Nnpb, "B1");
        var jobOn = composition.SeedJobOnWithCmContext(
            "ref-RD2A", "pn-RD2A", "B1", tool.ToolId.Value, ToolType.Cm, tool.Reference, tool.Lot);
        var cm = jobOn.Contexts.First(context => context.ContextType == ToolContextType.Cm);
        var pesoId = await SeedSubmittedPesoWithRowsAsync(composition, cm.ContextId, rowCount: 1);

        // Create-granted host renders the submitted draft read-only view; Approve-granted host
        // renders the review view. Both hosts share the SAME composition (same persisted facts).
        using var createFactory = P2T06TestHost.ForUser(P2T06TestHost.CreateOnly(), composition);
        using var createClient = createFactory.CreateClient();
        using var createResponse = await P2T06TestHost.GetAsync(
            createClient,
            $"/controlo/create?pesoId={pesoId}");
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var createHtml = await createResponse.Content.ReadAsStringAsync();

        using var approveFactory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var approveClient = approveFactory.CreateClient();
        using var approveResponse = await P2T06TestHost.GetAsync(
            approveClient,
            $"{ApprovePath}?pesoId={pesoId}");
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approveHtml = await approveResponse.Content.ReadAsStringAsync();

        // The operational facts are rendered through the SAME hooks with the same normalization:
        // text hooks (dd/td) and input-value hooks (Create's readonly submitted view) are each
        // extracted from the element that carries the hook and compared 1:1.
        var parities = new (string Name, string Key)[]
        {
            ("data-dmo-strip-reference", @"data-dmo-strip-reference=""true"""),
            ("data-dmo-strip-production", @"data-dmo-strip-production=""true"""),
            ("data-dmo-strip-machine", @"data-dmo-strip-machine=""true"""),
            ("data-dmo-field-temperature", @"data-dmo-field-temperature=""true"""),
            ("data-dmo-field-marisa", @"data-dmo-field-marisa=""true"""),
            ("data-dmo-field-puncao", @"data-dmo-field-puncao=""true"""),
            ("data-dmo-field-sap-end", @"data-dmo-field-sap-end=""true"""),
            ("data-dmo-field-sap-weight", @"data-dmo-field-sap-weight=""true"""),
            ("result capacity row 1", @"data-dmo-result-capacity=""1"""),
            ("result glass row 1", @"data-dmo-result-glass=""1"""),
            ("data-dmo-summary-density", @"data-dmo-summary-density"),
        };

        foreach (var (name, key) in parities)
        {
            var fromCreate = FactOf(createHtml, key);
            var fromApprove = FactOf(approveHtml, key);
            Assert.True(
                !string.IsNullOrEmpty(fromCreate) && string.Equals(fromCreate, fromApprove, StringComparison.Ordinal),
                $"Renderer parity failed on '{name}': Create='{fromCreate}' Approve='{fromApprove}'.");
        }

        // The frozen triple (same label/order) appears on both sides.
        Assert.Contains("data-dmo-frozen-triple=\"true\"", createHtml, StringComparison.Ordinal);
        Assert.Contains("data-dmo-frozen-triple=\"true\"", approveHtml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Owner slice (Create ← decision result): after the responsible decides on the SAME
    /// <c>peso_id</c>, Controlo_Create states the result clearly — the canonical status
    /// (Aprovado / Não aprovado) with the decision tone, the read-only presentation kept, and NO
    /// misleading "submetido para aprovação" wording. A decided record is never re-editable from
    /// Create; the correction path of a rejection (Reabrir em Aprovar) is stated — the app
    /// informs and records, it never blocks.
    /// </summary>
    [Fact]
    public async Task DEC_TheCreatePageStatesTheDecisionResultOfTheSamePesoClearly()
    {
        // Approved scenario (decision applied on the exact submitted peso).
        var approvedComposition = new P2T06TestComposition();
        var approvedId = await SeedSubmittedPesoAsync(approvedComposition, "DEC-OK");
        var approvedPeso = (await approvedComposition.Pesos.GetByIdAsync(approvedId, CancellationToken.None))!;
        await approvedComposition.Review.DecisionAsync(
            new DMO.Domain.Controlo.PesoReviewDecision(
                DMO.Domain.Controlo.PesoReviewDecisionId.New(),
                DMO.Domain.Controlo.PesoId.From(approvedId),
                DMO.Domain.Controlo.PesoReviewDecisionKind.Aprovado,
                P2T06TestHost.ActorUserId,
                DateTimeOffset.UtcNow,
                Reason: null,
                DMO.Domain.Controlo.PesoStatus.Pendente,
                approvedPeso.Version,
                DateTimeOffset.UtcNow),
            expectedPesoVersion: approvedPeso.Version,
            CancellationToken.None);

        // Rejected scenario (decision + recorded reason on the exact submitted peso).
        var rejectedComposition = new P2T06TestComposition();
        var rejectedId = await SeedSubmittedPesoAsync(rejectedComposition, "DEC-NOK");
        var rejectedPeso = (await rejectedComposition.Pesos.GetByIdAsync(rejectedId, CancellationToken.None))!;
        await rejectedComposition.Review.DecisionAsync(
            new DMO.Domain.Controlo.PesoReviewDecision(
                DMO.Domain.Controlo.PesoReviewDecisionId.New(),
                DMO.Domain.Controlo.PesoId.From(rejectedId),
                DMO.Domain.Controlo.PesoReviewDecisionKind.NaoAprovado,
                P2T06TestHost.ActorUserId,
                DateTimeOffset.UtcNow,
                "Leitura fora do esperado.",
                DMO.Domain.Controlo.PesoStatus.Pendente,
                rejectedPeso.Version,
                DateTimeOffset.UtcNow),
            expectedPesoVersion: rejectedPeso.Version,
            CancellationToken.None);

        using var approvedFactory = P2T06TestHost.ForUser(P2T06TestHost.CreateOnly(), approvedComposition);
        using var approvedClient = approvedFactory.CreateClient();
        using var approvedResponse = await P2T06TestHost.GetAsync(
            approvedClient,
            $"/controlo/create?pesoId={approvedId}");
        Assert.Equal(HttpStatusCode.OK, approvedResponse.StatusCode);
        var approvedHtml = await approvedResponse.Content.ReadAsStringAsync();

        // Approved: canonical status + success tone; NO pending/submitted wording anywhere.
        Assert.Equal("Aprovado", FactOf(approvedHtml, "data-dmo-status-text="));
        Assert.Contains("data-dmo-status-tone=\"success\"", approvedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("submetido", approvedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Pendente", approvedHtml, StringComparison.Ordinal);

        using var rejectedFactory = P2T06TestHost.ForUser(P2T06TestHost.CreateOnly(), rejectedComposition);
        using var rejectedClient = rejectedFactory.CreateClient();
        using var rejectedResponse = await P2T06TestHost.GetAsync(
            rejectedClient,
            $"/controlo/create?pesoId={rejectedId}");
        Assert.Equal(HttpStatusCode.OK, rejectedResponse.StatusCode);
        var rejectedHtml = await rejectedResponse.Content.ReadAsStringAsync();

        // Rejected: canonical status (Razor hex-encodes the ã in the raw markup) + danger tone +
        // the stated correction path; never presented as approved.
        Assert.Equal("Não aprovado", FactOf(rejectedHtml, "data-dmo-status-text="));
        Assert.Contains("data-dmo-status-tone=\"danger\"", rejectedHtml, StringComparison.Ordinal);
        Assert.Contains("Reabrir em Aprovar", rejectedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("submetido", rejectedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("data-dmo-status-text=\"Aprovado\"", rejectedHtml, StringComparison.Ordinal);

        // Both decided views keep the valid read-only presentation (D1 shape): only the disabled
        // submit action remains, no edit actions, adapter state intact for the page JS.
        foreach (var html in new[] { approvedHtml, rejectedHtml })
        {
            Assert.Contains("data-dmo-submitted=\"true\"", html, StringComparison.Ordinal);
            Assert.Equal(1, Count(html, "data-dmo-action=\"submit\""));
            Assert.Contains("data-dmo-action-reason=\"submit\"", html, StringComparison.Ordinal);
            Assert.Equal(0, Count(html, "data-dmo-action=\"calculate\""));
            Assert.Equal(0, Count(html, "data-dmo-action=\"save\""));
            Assert.Contains("data-dmo-controlo-state", html, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The rendered display VALUE of one hook: finds the element tag carrying the hook, then reads
    /// the <c>value</c> attribute when the element is an input, else the element's text content
    /// (HTML-decoded).
    /// </summary>
    private static string FactOf(string html, string attributeKey)
    {
        var tagPattern = $@"<(?<tag>input|dd|td|span)[^>]*{System.Text.RegularExpressions.Regex.Escape(attributeKey)}[^>]*>";
        var tagMatch = System.Text.RegularExpressions.Regex.Match(
            html, tagPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!tagMatch.Success)
        {
            return string.Empty;
        }

        var tag = tagMatch.Value;
        if (string.Equals(tagMatch.Groups["tag"].Value, "input", StringComparison.OrdinalIgnoreCase))
        {
            var value = System.Text.RegularExpressions.Regex.Match(tag, @"value=""(?<v>[^""]*)""");
            return value.Success ? System.Net.WebUtility.HtmlDecode(value.Groups["v"].Value) : string.Empty;
        }

        var close = System.Text.RegularExpressions.Regex.Match(
            html,
            $"{System.Text.RegularExpressions.Regex.Escape(tagMatch.Value)}(?<v>[^<]*?)</{tagMatch.Groups["tag"].Value}>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return close.Success
            ? System.Net.WebUtility.HtmlDecode(close.Groups["v"].Value.Trim())
            : string.Empty;
    }

    /// <summary>Counts non-overlapping occurrences of a fragment in rendered markup.</summary>
    private static int Count(string html, string fragment)
    {
        var count = 0;
        var index = 0;

        while ((index = html.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }

    /// <summary>
    /// P2-T08 documents slice (render part) — the Peso PDF generation action is a CONTROL O
    /// CREATE surface (Create owns the operational document work after the decision): the Aprovar
    /// review sheet NEVER renders it (Approve stays focused on approve/reject/reopen, decided and
    /// undecided alike), while the Create page for a decided, production-bound draft renders
    /// <c>Gerar PDF do Peso</c> with its outcome region and keeps the document seam otherwise.
    /// </summary>
    [Fact]
    public async Task PD1_TheDecisionSurfaceStaysFocusedAndCreateOffersThePesoPdfAction()
    {
        // Approved + production-bound: the Approve review sheet does NOT offer the action.
        var approvedComposition = new P2T06TestComposition();
        var approvedId = await SeedSubmittedPesoAsync(approvedComposition, "PD1A");
        var approvedPeso = (await approvedComposition.Pesos.GetByIdAsync(approvedId, CancellationToken.None))!;
        await approvedComposition.Review.DecisionAsync(
            new DMO.Domain.Controlo.PesoReviewDecision(
                DMO.Domain.Controlo.PesoReviewDecisionId.New(),
                DMO.Domain.Controlo.PesoId.From(approvedId),
                DMO.Domain.Controlo.PesoReviewDecisionKind.Aprovado,
                P2T06TestHost.ActorUserId,
                DateTimeOffset.UtcNow,
                Reason: null,
                DMO.Domain.Controlo.PesoStatus.Pendente,
                approvedPeso.Version,
                DateTimeOffset.UtcNow),
            expectedPesoVersion: approvedPeso.Version,
            CancellationToken.None);

        using (var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), approvedComposition))
        using (var client = factory.CreateClient())
        using (var response = await P2T06TestHost.GetAsync(client, $"{ApprovePath}?pesoId={approvedId}"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();

            Assert.DoesNotContain("data-dmo-peso-pdf", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Gerar PDF", html, StringComparison.Ordinal);
        }

        // The SAME decided record, opened through Controlo Create: the action IS offered there.
        using (var factory = P2T06TestHost.ForUser(P2T06TestHost.CreateOnly(), approvedComposition))
        using (var client = factory.CreateClient())
        using (var response = await P2T06TestHost.GetAsync(client, $"/controlo/create?pesoId={approvedId}"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Contains("data-dmo-peso-pdf=\"true\"", html, StringComparison.Ordinal);
            Assert.Contains("data-dmo-peso-pdf-outcome=\"true\"", html, StringComparison.Ordinal);
            Assert.Contains("Gerar PDF do Peso", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-dmo-documents-seam", html, StringComparison.Ordinal);
        }

        // Submitted but UNDECIDED: neither surface offers the action; Create keeps the seam.
        var undecidedComposition = new P2T06TestComposition();
        var undecidedId = await SeedSubmittedPesoAsync(undecidedComposition, "PD1B");

        using (var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), undecidedComposition))
        using (var client = factory.CreateClient())
        using (var response = await P2T06TestHost.GetAsync(client, $"{ApprovePath}?pesoId={undecidedId}"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();

            Assert.DoesNotContain("data-dmo-peso-pdf", html, StringComparison.Ordinal);
        }

        using (var factory = P2T06TestHost.ForUser(P2T06TestHost.CreateOnly(), undecidedComposition))
        using (var client = factory.CreateClient())
        using (var response = await P2T06TestHost.GetAsync(client, $"/controlo/create?pesoId={undecidedId}"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();

            Assert.DoesNotContain("data-dmo-peso-pdf", html, StringComparison.Ordinal);
            Assert.Contains("data-dmo-documents-seam", html, StringComparison.Ordinal);
        }
    }

    private static void AssertInOrder(string html, params string[] markers)
    {
        var index = -1;
        foreach (var marker in markers)
        {
            var found = html.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(found > index, $"Marker '{marker}' is out of order (or missing).");
            index = found;
        }
    }
}