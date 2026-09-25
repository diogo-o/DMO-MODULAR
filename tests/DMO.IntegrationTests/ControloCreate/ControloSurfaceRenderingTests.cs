using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DMO.Application.Access;
using DMO.Domain.JobOn;
using DMO.Domain.Tools;
using DMO.IntegrationTests.JobOn;

namespace DMO.IntegrationTests.ControloCreate;

/// <summary>
/// P2-T05 rendered-surface (<c>R</c>) proofs: the REAL Razor surfaces of the Controlo Create area,
/// rendered by the REAL host over the controlled store, at the canonical fixed desktop composition.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §8.3 (regions R1–R8), §24 (the fixed desktop binding), §26.4 rows
/// LAY2/LAY3 (AC-N1). The region-stable structure and the settings-section order are asserted on the
/// RENDERED pages; the keyboard-reachable local overflow containers and the full heading sets are
/// asserted on the page MARKUP (via <see cref="P2T04ProductionScan"/>), and the breakpoint-free
/// stylesheet is read from the repository.</remarks>
public sealed class ControloSurfaceRenderingTests
{
    private const string CreatePath = "/controlo/create";
    private const string DefinicoesPath = "/controlo/create/definicoes";

    /// <summary>
    /// LAY2 (contract §26.4) — proves AC-N1: the rendered Create page keeps the eight regions in
    /// their structural order (strip → selection → cm-context → inputs → results → actions →
    /// status → documents) for a granted caller opening a production with a determined CM context.
    /// </summary>
    [Fact]
    public async Task LAY2_CreatePageRendersTheRegionsInTheirStructuralOrder()
    {
        var composition = new P2T05TestComposition();
        var tool = composition.SeedTool(ToolType.Cm, "5447T173", "LOTE-LAY2", Processo.Nnpb);
        var jobOn = composition.SeedJobOnWithCmContext(
            "LAY2-TEST", "1000", "B1", tool.ToolId.Value, ToolType.Cm, tool.Reference, tool.Lot);

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T05TestHost.GetAsync(client, $"{CreatePath}?jobonId={jobOn.JobOnId.Value}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        AssertInOrder(html,
            "data-dmo-controlo-strip=\"true\"",
            "data-dmo-controlo-region=\"selection\"",
            "data-dmo-controlo-region=\"cm-context\"",
            "data-dmo-controlo-region=\"inputs\"",
            "data-dmo-controlo-region=\"results\"",
            "data-dmo-controlo-region=\"actions\"",
            "data-dmo-controlo-region=\"status\"",
            "data-dmo-controlo-region=\"documents\"");

        // The determined context renders its frozen triple (the region is not empty).
        Assert.Contains("data-dmo-frozen-triple=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("5447T173", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// LAY2 (contract §26.4; §34.3 delta) — proves AC-N1: the rendered Definições page keeps the
    /// FOUR remaining section titles in their structural order (the repairer family — the former
    /// first two sections — MOVED to <c>Boquilhas > Definições</c> per the Owner clarification
    /// P2-T07 §34.3 / P2-T05 §31.3; the glass-density section is the approved correction addition),
    /// and the larger-desktop composition is preserved by the static rule that the linked
    /// <c>dmo-controlo.css</c> carries no breakpoint rule.
    /// </summary>
    [Fact]
    public async Task LAY2_DefinicoesPageRendersTheFourSectionsInOrderAndLinksTheBreakpointFreeStylesheet()
    {
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T05TestHost.GetAsync(client, DefinicoesPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        AssertInOrder(html,
            "Diretório de PDF/documentos",
            "Listas de email",
            "Templates de email",
            "Densidade do vidro (g/cm³)");

        // The glass-density section renders exactly the two fixed canonical entries with the
        // current operational values and the g/cm³ unit (correction contract §5.3).
        Assert.Equal(2, Count(html, "data-dmo-glass-density-row=\""));
        Assert.Contains("data-dmo-glass-density-row=\"NNPB\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-glass-density-row=\"PS\"", html, StringComparison.Ordinal);
        Assert.Equal(2, Count(html, ">g/cm³<"));
        Assert.Contains("step=\"0.0001\"", html, StringComparison.Ordinal);
        Assert.Contains("2.4027", html, StringComparison.Ordinal);
        Assert.Contains("2.4231", html, StringComparison.Ordinal);

        // The rendered page links the P2-T05 stylesheet, and that stylesheet is breakpoint-free
        // (LAY1 covers the full static rule; this row proves the rendered page actually links it).
        Assert.Contains("dmo-controlo.css", html, StringComparison.Ordinal);

        var css = P2T04ProductionScan.WithoutCssComments(
            P2T04ProductionScan.Read("src/DMO.Web/wwwroot/css/dmo-controlo.css"));

        Assert.False(string.IsNullOrWhiteSpace(css), "The P2-T05 stylesheet must carry real rules.");
        Assert.DoesNotContain("@media", css, StringComparison.Ordinal);
    }

    /// <summary>
    /// LAY3 (contract §26.4; §34.3 delta) — proves AC-N1: the results table (Create markup) and
    /// the THREE remaining settings tables (Definições markup — the repairers/assignments tables
    /// MOVED to <c>Boquilhas > Definições</c>; the glass-density table is the approved correction
    /// addition) are wrapped in keyboard-reachable local overflow containers
    /// (<c>role="region"</c> + <c>tabindex="0"</c>), and no required column is hidden: every
    /// results heading is present in full.
    /// </summary>
    [Fact]
    public void LAY3_ResultsAndSettingsTablesUseKeyboardReachableLocalOverflowContainers()
    {
        var createMarkup = P2T04ProductionScan.Read("src/DMO.Web/Pages/Controlo/Create.cshtml");
        var definicoesMarkup = P2T04ProductionScan.Read("src/DMO.Web/Pages/Controlo/Definicoes.cshtml");

        // The results table sits inside the local scroll container, which is keyboard reachable.
        var resultsScroll = Regex.Match(
            createMarkup,
            "<div\\b[^>]*data-dmo-controlo-results-scroll=\"true\"[^>]*>",
            RegexOptions.IgnoreCase);

        Assert.True(resultsScroll.Success, "The results region must carry a local scroll container.");
        Assert.Contains("role=\"region\"", resultsScroll.Value, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"0\"", resultsScroll.Value, StringComparison.Ordinal);

        Assert.True(
            createMarkup.IndexOf("data-dmo-controlo-results-scroll=\"true\"", StringComparison.Ordinal)
            < createMarkup.IndexOf("data-dmo-results-table", StringComparison.Ordinal),
            "The results table must be rendered INSIDE its scroll container.");

        // The three remaining settings tables each sit inside a keyboard-reachable local scroll
        // container, in the same sequence as their regions (the §34.3 delta removed the
        // repairers/assignments sections — the repairer family moved to Boquilhas > Definições).
        var settingsScrolls = Regex.Matches(
                definicoesMarkup,
                "<div\\b[^>]*class=\"[^\"]*dmo-controlo__settings-scroll[^\"]*\"[^>]*>",
                RegexOptions.IgnoreCase)
            .Cast<Match>()
            .ToArray();

        Assert.Equal(3, settingsScrolls.Length);
        Assert.All(settingsScrolls, scroll =>
        {
            Assert.Contains("role=\"region\"", scroll.Value, StringComparison.Ordinal);
            Assert.Contains("tabindex=\"0\"", scroll.Value, StringComparison.Ordinal);
        });

        var settingsTables = new[]
        {
            "data-dmo-lists-table",
            "data-dmo-templates-table",
            "data-dmo-glass-densities-table",
        };

        for (var index = 0; index < settingsTables.Length; index++)
        {
            var tableIndex = definicoesMarkup.IndexOf(settingsTables[index], StringComparison.Ordinal);
            var scrollIndex = definicoesMarkup.IndexOf(settingsScrolls[index].Value, StringComparison.Ordinal);
            var nextScrollIndex = index + 1 < settingsScrolls.Length
                ? definicoesMarkup.IndexOf(settingsScrolls[index + 1].Value, StringComparison.Ordinal)
                : definicoesMarkup.Length;

            Assert.True(
                tableIndex > scrollIndex && tableIndex < nextScrollIndex,
                $"The settings table '{settingsTables[index]}' must be INSIDE its own scroll container.");
        }

        // No required results column is hidden: every heading is present in full text.
        foreach (var heading in new[]
                 {
                     ">Leitura<",
                     ">Peso em água (g)<",
                     ">Capacidade (cm³)<",
                     ">Desvio cm³<",
                     ">Desvio %<",
                     ">Peso do vidro (g)<",
                 })
        {
            Assert.Contains(heading, createMarkup, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// LAY3 (contract §26.4) — proves AC-N1: actions never move between regions — the
    /// <c>actions</c> region appears exactly once in the RENDERED Create page and contains the
    /// decision-bar partial (the <c>data-dmo-decision-bar</c> marker sits between the actions
    /// region and the following status region).
    /// </summary>
    [Fact]
    public async Task LAY3_TheDecisionBarIsRenderedInsideTheActionsRegionOnly()
    {
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T05TestHost.GetAsync(client, CreatePath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(1, Count(html, "data-dmo-controlo-region=\"actions\""));
        Assert.Equal(1, Count(html, "data-dmo-decision-bar=\"true\""));

        var actionsIndex = html.IndexOf("data-dmo-controlo-region=\"actions\"", StringComparison.Ordinal);
        var decisionBarIndex = html.IndexOf("data-dmo-decision-bar=\"true\"", StringComparison.Ordinal);
        var statusIndex = html.IndexOf("data-dmo-controlo-region=\"status\"", StringComparison.Ordinal);

        Assert.True(actionsIndex >= 0, "The actions region must be rendered.");
        Assert.True(
            actionsIndex < decisionBarIndex && decisionBarIndex < statusIndex,
            "The decision bar must be rendered INSIDE the actions region, before the status region.");
    }

    /// <summary>
    /// WDL1 (Owner clarification WATER_TEMPERATURE_TO_WATER_DENSITY_LOOKUP, requirement 1 + 6) —
    /// the rendered operator surface shows exactly ONE water-input: the
    /// <c>Temperatura da água (°C)</c> field (5–35). There is NO second field for water density
    /// (or any divisor/calculation-factor input): the density is resolved automatically by the
    /// application. The operator workflow is exactly
    /// <c>[ Temperatura da água: ____ °C ]</c>.
    /// </summary>
    [Fact]
    public async Task WDL1_TheOperatorSurfaceRendersOnlyTheWaterTemperatureInput_NoDensityInput()
    {
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await P2T05TestHost.GetAsync(client, CreatePath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        // The temperature input: label + exactly one number field named waterTemperature, 5–35.
        Assert.Contains("Temperatura da água (°C)", html, StringComparison.Ordinal);
        Assert.Equal(1, Count(html, "name=\"waterTemperature\""));
        Assert.Contains("min=\"5\"", html, StringComparison.Ordinal);
        Assert.Contains("max=\"35\"", html, StringComparison.Ordinal);

        // NO water-density/divisor input exists anywhere on the surface.
        Assert.DoesNotContain("name=\"waterDensity\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"divisor\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Densidade da água", html, StringComparison.Ordinal);
        Assert.DoesNotContain("dmo-controlo-density", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// D1 focused regression (RENDERED proof of the pre-fix crash condition) — opening a SUBMITTED
    /// Peso renders the valid read-only state that the pre-fix adapter crashed on: the editable
    /// actions (calculate/save/cancel) are intentionally ABSENT, the submitted-state decision bar
    /// keeps exactly the disabled <c>submit</c> action ("Submetido" + visible reason), and the
    /// adapter state region the JS must initialize against is still rendered. The behavioral half
    /// of the D1 regression (the adapter initializing safely over this markup) is proven by
    /// <see cref="DmoControloAdapterBehaviorTests"/>.
    /// </summary>
    /// <remarks>
    /// Authority: independent verification report §11 D1; contract §8.6 (submitted-only
    /// presentation) and §8.4/§8.5 (non-ready state behavior). Editable controls being absent is a
    /// VALID state — they must never be restored merely to satisfy JavaScript.
    /// </remarks>
    [Fact]
    public async Task D1_SubmittedViewOmitsEditableActionsAndKeepsTheDisabledSubmit()
    {
        var composition = new P2T05TestComposition();
        var tool = composition.SeedTool(ToolType.Cm, "5447T173", "LOTE-D1", Processo.Nnpb);
        var jobOn = composition.SeedJobOnWithCmContext(
            "D1-TEST", "1000", "B1", tool.ToolId.Value, ToolType.Cm, tool.Reference, tool.Lot);
        var cmId = jobOn.Contexts.Single(context => context.ContextType == ToolContextType.Cm).ContextId;

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        // Arrange the submitted Peso through the REAL routes (create → submit).
        Guid pesoId;
        using (var createResponse = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   "/controlo/create/pesos",
                   P2T05TestHost.Json(new
                   {
                       cmId,
                       pendingToolId = (Guid?)null,
                       waterTemperature = 20m,
                       volumeMarisaBq = (decimal?)null,
                       volumePuncaoPu = (decimal?)null,
                       previousProductionEndReference = (string?)null,
                       previousAverageWeightReference = (string?)null,
                       rows = new[] { new { waterWeightG = 500m } },
                   })))
        {
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            pesoId = created.RootElement.GetProperty("pesoId").GetGuid();
        }

        using (var submitResponse = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"/controlo/create/pesos/{pesoId}/submit",
                   P2T05TestHost.Json(new { expectedVersion = 1 })))
        {
            Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        }

        // Open the submitted draft: the surface renders the read-only submitted state.
        using var pageResponse = await P2T05TestHost.GetAsync(client, $"{CreatePath}?pesoId={pesoId}");
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);

        var html = await pageResponse.Content.ReadAsStringAsync();

        // The pre-fix crash condition: the editable actions are ABSENT (valid state).
        Assert.Equal(0, Count(html, "data-dmo-action=\"calculate\""));
        Assert.Equal(0, Count(html, "data-dmo-action=\"save\""));
        Assert.Equal(0, Count(html, "data-dmo-action=\"cancel\""));

        // The submitted-state UI keeps exactly the disabled submit action with its visible reason
        // (Razor renders the non-ASCII reason text HTML-encoded, e.g. j&#xE1; — assert the encoded
        // rendering, which is what the browser displays).
        Assert.Equal(1, Count(html, "data-dmo-action=\"submit\""));
        Assert.Contains("Submetido", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-action-reason=\"submit\"", html, StringComparison.Ordinal);
        Assert.Contains("foi submetido para aprova", html, StringComparison.Ordinal);

        // The adapter state region (the JS initialization target) is still rendered.
        Assert.Contains("data-dmo-controlo-state", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-peso-id", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-submitted=\"true\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// COMP-CREATE-1 — the Comparação workspace is rendered inside the existing Create/Peso
    /// context when a real <c>peso_id</c> is loaded. It reuses the SAME Peso sheet (current
    /// identity + real measurements/calculations), shows the previous-production side as
    /// unavailable and never links to a standalone comparison route.
    /// </summary>
    [Fact]
    public async Task COMP_CREATE1_ComparisonWorkspaceRendersInsidePesoCreateContext()
    {
        var composition = new P2T05TestComposition();
        var tool = composition.SeedTool(ToolType.Cm, "5447T173", "LOTE-COMP", Processo.Nnpb);
        var jobOn = composition.SeedJobOnWithCmContext(
            "COMP-CREATE", "1000", "B1", tool.ToolId.Value, ToolType.Cm, tool.Reference, tool.Lot);
        var cmId = jobOn.Contexts.Single(context => context.ContextType == ToolContextType.Cm).ContextId;

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        Guid pesoId;
        using (var createResponse = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   "/controlo/create/pesos",
                   P2T05TestHost.Json(new
                   {
                       cmId,
                       pendingToolId = (Guid?)null,
                       waterTemperature = 20m,
                       volumeMarisaBq = (decimal?)null,
                       volumePuncaoPu = (decimal?)null,
                       previousProductionEndReference = (string?)null,
                       previousAverageWeightReference = (string?)null,
                       rows = new[] { new { waterWeightG = 500m } },
                   })))
        {
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            pesoId = created.RootElement.GetProperty("pesoId").GetGuid();
        }

        using var pageResponse = await P2T05TestHost.GetAsync(client, $"/controlo/create?pesoId={pesoId}");
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);

        var html = await pageResponse.Content.ReadAsStringAsync();

        // The comparison region is rendered inside the Peso Create surface.
        Assert.Contains("data-dmo-controlo-region=\"comparison\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-comparacao-head=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-comparacao-identities=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-comparacao-current=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-comparacao-previous=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-comparacao-table=\"true\"", html, StringComparison.Ordinal);

        // Real current Peso facts are rendered; the previous side is truthful unavailable.
        Assert.Contains("CM 5447T173", html, StringComparison.Ordinal);
        Assert.Contains("500", html, StringComparison.Ordinal);
        Assert.Contains("Ainda não disponível", html, StringComparison.Ordinal);

        // No standalone comparison route remains.
        Assert.DoesNotContain("/controlo/comparacao", html, StringComparison.Ordinal);
        Assert.DoesNotContain("dmo-controlo-comparacao.js", html, StringComparison.Ordinal);

        // The comparison region sits between results and actions (inside the Peso page).
        var resultsIndex = html.IndexOf("data-dmo-controlo-region=\"results\"", StringComparison.Ordinal);
        var comparisonIndex = html.IndexOf("data-dmo-controlo-region=\"comparison\"", StringComparison.Ordinal);
        var actionsIndex = html.IndexOf("data-dmo-controlo-region=\"actions\"", StringComparison.Ordinal);
        Assert.True(resultsIndex >= 0 && comparisonIndex > resultsIndex && actionsIndex > comparisonIndex);
    }

    // ---- arrangement helpers -------------------------------------------------------------

    /// <summary>Asserts that the supplied fragments appear in the text in the supplied order.</summary>
    private static void AssertInOrder(string text, params string[] fragments)
    {
        var previous = -1;

        foreach (var fragment in fragments)
        {
            var index = text.IndexOf(fragment, StringComparison.Ordinal);

            Assert.True(
                index >= 0,
                $"The rendered page must contain '{fragment}'.");

            Assert.True(
                index > previous,
                $"The rendered page must contain '{fragment}' AFTER the previous region marker.");

            previous = index;
        }
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
}