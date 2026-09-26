using System.Net;
using System.Text.RegularExpressions;
using DMO.Domain.Tools;
using DMO.IntegrationTests.JobOn;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DMO.IntegrationTests.Controlo.Pesos;

/// <summary>
/// Focused proofs of the Controlo -> Resumo landing page (<c>/controlo/resumo</c>, plus the
/// <c>/controlo</c> landing redirect): the REAL web host and Razor page over the controlled
/// P2-T05/P2-T04 stores, same composition as the accepted P2-T05 Controlo Create tests.
/// </summary>
/// <remarks>
/// <para>
/// Covers exactly the Resumo slice's functional target: the page always opens; the reference
/// search loads the reference's Job On productions; the productions are listed NEWEST FIRST;
/// production selection is explicit (a route map plus an open control, never an auto-open); the
/// design direct link <c>?ref=&lt;reference&gt;&amp;production=&lt;production&gt;</c> and the
/// canonical <c>?jobonId=</c> both load the SAME <c>IProductionResumoRead</c> projection; and
/// no prototype/demo data (sessionStorage, MCaliper links, Guardar Resumo, revision counters,
/// invented sheet status) appears anywhere in the page.
/// </para>
/// </remarks>
public sealed class ControloResumoPageTests
{
    private const string ResumoPath = "/controlo/resumo";

    /// <summary>RESUMO-PAGE-1 — the page always opens: no query, no seeded data, still 200 with
    /// the lookup surface and no sheet; and the Controlo landing <c>/controlo</c> redirects to it.</summary>
    [Fact]
    public async Task RESUMO_PAGE1_PageAlwaysOpensAndControloLandsOnIt()
    {
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using (var response = await P2T05TestHost.GetAsync(client, ResumoPath))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("data-dmo-resumo-surface=\"true\"", html, StringComparison.Ordinal);
            Assert.Contains("data-dmo-resumo-lookup=\"true\"", html, StringComparison.Ordinal);
            Assert.Contains("data-dmo-strip-empty=\"true\"", html, StringComparison.Ordinal);
            // The secondary Controlo tabs render with Resumo current.
            Assert.Contains("data-dmo-controlo-tabs=\"true\"", html, StringComparison.Ordinal);
            Assert.Contains("data-dmo-controlo-tab=\"peso\"", html, StringComparison.Ordinal);
            // No production is selected: no sheet facts are fabricated.
            Assert.DoesNotContain("data-dmo-strip-reference", html, StringComparison.Ordinal);
        }

        // The Controlo landing route redirects to the Resumo page, preserving the direct link.
        using (var landingClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }))
        {
            using var landing = await P2T05TestHost.GetAsync(landingClient, "/controlo?ref=ABC&production=1001");
            Assert.Equal(HttpStatusCode.Found, landing.StatusCode);
            Assert.Equal("/controlo/resumo?ref=ABC&production=1001", landing.Headers.Location?.OriginalString);
        }
    }

    /// <summary>RESUMO-PAGE-2 — the reference search loads the reference's Job On productions
    /// and lists them NEWEST FIRST for explicit selection (never auto-selected, never auto-opened).</summary>
    [Fact]
    public async Task RESUMO_PAGE2_ReferenceSearchListsProductionsNewestFirst()
    {
        var composition = new P2T05TestComposition();
        composition.JobOnToolStore.SeedJobOn("R-RES-2", "1000", "B1", new DateOnly(2026, 1, 5));
        composition.JobOnToolStore.SeedJobOn("R-RES-2", "1002", "B2", new DateOnly(2026, 3, 1));
        composition.JobOnToolStore.SeedJobOn("R-RES-2", "1001", "C3", new DateOnly(2026, 2, 10));

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var page = await P2T05TestHost.GetAsync(client, $"{ResumoPath}?ref=R-RES-2");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        var html = await page.Content.ReadAsStringAsync();

        Assert.Contains("data-dmo-resumo-route-map=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-dense-table=\"true\"", html, StringComparison.Ordinal);
        Assert.Equal(3, Occurrences(html, "data-dmo-resumo-route=\""));

        // Newest first: 1002 (2026-03-01), then 1001 (2026-02-10), then 1000 (2026-01-05).
        // The shared DenseDataTable renders cell text on its own whitespace-padded line inside
        // the <td>, so the probe tolerates the component-owned surrounding whitespace.
        var i1002 = FirstCellTextIndex(html, "1002");
        var i1001 = FirstCellTextIndex(html, "1001");
        var i1000 = FirstCellTextIndex(html, "1000");
        Assert.True(i1002 >= 0 && i1001 > i1002 && i1000 > i1001,
            $"expected newest-first order 1002,1001,1000; got indexes {i1002},{i1001},{i1000}");

        // Every switcher row links to the design's direct link for ITS production.
        Assert.Contains("/controlo/resumo?ref=R-RES-2&amp;production=1002", html, StringComparison.Ordinal);
        Assert.Contains("/controlo/resumo?ref=R-RES-2&amp;production=1000", html, StringComparison.Ordinal);

        // Explicit selection only: no row is pre-selected and no sheet is loaded from a search alone.
        Assert.DoesNotContain("aria-selected=\"true\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-dmo-strip-reference", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http-equiv=\"refresh\"", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>RESUMO-PAGE-3 — production selection: the selected jobon_id loads the correct
    /// <c>IProductionResumoRead</c> projection (strip facts + CM context), and the direct link
    /// <c>?ref=&amp;production=</c> resolves the SAME sheet; switching production replaces the
    /// whole production context.</summary>
    [Fact]
    public async Task RESUMO_PAGE3_SelectionLoadsTheCorrectProjectionAndSwitchingReplacesIt()
    {
        var composition = new P2T05TestComposition();
        var toolA = composition.SeedTool(ToolType.Cm, "5447T173", "LOTE-A", Processo.Nnpb, quantity: 9, "B1");
        var toolB = composition.SeedTool(ToolType.Cm, "5447T199", "LOTE-B", Processo.Ps, quantity: 4, "C3");
        var older = composition.SeedJobOnWithCmContext(
            "R-RES-3", "2001", "B1", toolA.ToolId.Value, ToolType.Cm, toolA.Reference, toolA.Lot,
            new DateOnly(2026, 4, 2));
        var newer = composition.SeedJobOnWithCmContext(
            "R-RES-3", "3002", "C3", toolB.ToolId.Value, ToolType.Cm, toolB.Reference, toolB.Lot,
            new DateOnly(2026, 5, 2));

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        // By the canonical jobon anchor.
        using (var byAnchor = await P2T05TestHost.GetAsync(client, $"{ResumoPath}?jobonId={older.JobOnId.Value}"))
        {
            Assert.Equal(HttpStatusCode.OK, byAnchor.StatusCode);
            var html = await byAnchor.Content.ReadAsStringAsync();
            Assert.Contains("data-dmo-strip-reference=\"true\">R-RES-3", html, StringComparison.Ordinal);
            Assert.Contains("data-dmo-strip-production=\"true\">2001", html, StringComparison.Ordinal);
            Assert.Contains("data-dmo-strip-machine=\"true\">B1", html, StringComparison.Ordinal);
            Assert.Contains("data-dmo-strip-date=\"true\">2026-04-02", html, StringComparison.Ordinal);
            Assert.Contains("data-dmo-strip-processo=\"true\">NNPB", html, StringComparison.Ordinal);
            Assert.Contains("data-dmo-frozen-triple=\"true\"", html, StringComparison.Ordinal);
            Assert.Contains("LOTE-A", html, StringComparison.Ordinal);
            // The Peso card links to the existing Peso surface anchored on the SAME jobon_id.
            Assert.Contains($"href=\"/controlo/create?jobonId={older.JobOnId.Value}\"", html, StringComparison.Ordinal);
        }

        // By the design direct link: same sheet, reached through ?ref=&production=.
        using (var byLink = await P2T05TestHost.GetAsync(client, $"{ResumoPath}?ref=R-RES-3&production=2001"))
        {
            Assert.Equal(HttpStatusCode.OK, byLink.StatusCode);
            var html = await byLink.Content.ReadAsStringAsync();
            Assert.Contains("data-dmo-strip-production=\"true\">2001", html, StringComparison.Ordinal);
            Assert.Contains("LOTE-A", html, StringComparison.Ordinal);
            Assert.DoesNotContain("LOTE-B", html, StringComparison.Ordinal);
            // The loaded row is marked in the switcher, never auto-opened elsewhere.
            Assert.Contains("aria-selected=\"true\"", html, StringComparison.Ordinal);
            // The Peso access card anchors on the SAME jobon_id.
            Assert.Contains($"href=\"/controlo/create?jobonId={older.JobOnId.Value}\"", html, StringComparison.Ordinal);
        }

        // Switching production replaces the whole production context.
        using (var switched = await P2T05TestHost.GetAsync(client, $"{ResumoPath}?ref=R-RES-3&production=3002"))
        {
            Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
            var html = await switched.Content.ReadAsStringAsync();
            Assert.Contains("data-dmo-strip-production=\"true\">3002", html, StringComparison.Ordinal);
            Assert.Contains("data-dmo-strip-machine=\"true\">C3", html, StringComparison.Ordinal);
            Assert.Contains("data-dmo-strip-processo=\"true\">PS", html, StringComparison.Ordinal);
            Assert.Contains("LOTE-B", html, StringComparison.Ordinal);
            Assert.DoesNotContain("LOTE-A", html, StringComparison.Ordinal);
            Assert.Contains($"href=\"/controlo/create?jobonId={newer.JobOnId.Value}\"", html, StringComparison.Ordinal);
        }
    }

    /// <summary>RESUMO-PAGE-4 — truthful negative states: an unknown jobon_id and a direct link
    /// naming a production the reference does not have both open the page with a not-found
    /// state and never fabricate a sheet.</summary>
    [Fact]
    public async Task RESUMO_PAGE4_UnknownAnchorsAreTruthfulNotFound()
    {
        var composition = new P2T05TestComposition();
        composition.JobOnToolStore.SeedJobOn("R-RES-4", "4001", "B1", new DateOnly(2026, 1, 5));

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using (var unknown = await P2T05TestHost.GetAsync(client, $"{ResumoPath}?jobonId={Guid.NewGuid()}"))
        {
            Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
            var html = await unknown.Content.ReadAsStringAsync();
            Assert.Contains("data-dmo-resumo-not-found=\"true\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-dmo-strip-reference", html, StringComparison.Ordinal);
        }

        using (var noProduction = await P2T05TestHost.GetAsync(client, $"{ResumoPath}?ref=R-RES-4&production=9999"))
        {
            Assert.Equal(HttpStatusCode.OK, noProduction.StatusCode);
            var html = await noProduction.Content.ReadAsStringAsync();
            Assert.Contains("data-dmo-resumo-production-not-found=\"true\"", html, StringComparison.Ordinal);
            // The switcher still lists what DOES exist.
            Assert.Contains("4001", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-dmo-strip-reference", html, StringComparison.Ordinal);
        }
    }

    /// <summary>RESUMO-PAGE-5 — no fake or prototype data: the sheet renders only facts the
    /// current backend projection carries. The prototype's Guardar Resumo action, Job On
    /// revisão counter, sheet Estado, MCaliper links and demo fixtures never appear.</summary>
    [Fact]
    public async Task RESUMO_PAGE5_NoFakeOrPrototypeDataAppears()
    {
        var composition = new P2T05TestComposition();
        var tool = composition.SeedTool(ToolType.Cm, "5447T173", "LOTE-REAL", Processo.Nnpb, quantity: 9, "B1");
        var jobOn = composition.SeedJobOnWithCmContext(
            "R-RES-5", "5001", "B1", tool.ToolId.Value, ToolType.Cm, tool.Reference, tool.Lot,
            new DateOnly(2026, 4, 2));

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var page = await P2T05TestHost.GetAsync(client, $"{ResumoPath}?jobonId={jobOn.JobOnId.Value}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();

        // Prototype-only affordances and demo fixtures never appear.
        Assert.DoesNotContain("Guardar Resumo", html, StringComparison.Ordinal);
        Assert.DoesNotContain("MCaliper", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("revisão", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("7080C002", html, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", html, StringComparison.Ordinal);

        // Only real seeded facts are rendered.
        Assert.Contains("data-dmo-strip-reference=\"true\">R-RES-5", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-strip-production=\"true\">5001", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-strip-cm=\"true\">5447T173", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-strip-lot=\"true\">LOTE-REAL", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-strip-processo=\"true\">NNPB", html, StringComparison.Ordinal);

        // The tabs expose real routes: Peso and Definições are links; Pegamentos renders as a
        // truthful unavailable state. Comparação is NOT a Controlo destination — the comparison
        // workflow is the Peso-during-production process served inside the Peso Create surface
        // (same peso_id), so no tab and no standalone route exist for it.
        Assert.Contains("data-dmo-controlo-tab=\"peso\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/controlo/create?jobonId={jobOn.JobOnId.Value}\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/controlo/create/definicoes\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-controlo-tab=\"pegamentos\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-resumo-pegamentos-unavailable=\"true\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-dmo-controlo-tab=\"comparacao\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/controlo/comparacao", html, StringComparison.Ordinal);
    }

    private static int Occurrences(string haystack, string needle) =>
        haystack.Split(needle, StringSplitOptions.None).Length - 1;

    /// <summary>First position of a table cell carrying exactly <paramref name="cellText"/>,
    /// tolerating the shared component's whitespace around the text; -1 when absent.</summary>
    private static int FirstCellTextIndex(string html, string cellText)
    {
        var match = Regex.Match(html, @">\s*" + Regex.Escape(cellText) + @"\s*<");
        return match.Success ? match.Index : -1;
    }
}
