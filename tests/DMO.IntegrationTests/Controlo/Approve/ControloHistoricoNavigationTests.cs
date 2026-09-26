using System.Net;
using Xunit;

namespace DMO.IntegrationTests.Controlo.Approve;

/// <summary>
/// Histórico navigation slice: the real P2-T06 Histórico de Pesos page joined the secondary
/// Controlo navigation the Resumo landing page established. Proves the page renders the shared
/// tab strip with Histórico current, the Resumo tab links back to /controlo/resumo preserving the
/// production context through the pages' existing query contracts, Peso and Definições keep their
/// real routes when the caller holds Controlo Create, Pegamentos stays a truthful
/// unavailable state, and the capability gates still hold. Comparação is deliberately absent from
/// the strip: it is NOT a Controlo destination — it is the Peso-during-production workflow served
/// inside the existing Peso Create/Approve surfaces on the same <c>peso_id</c>. No history rows
/// are seeded because navigation is independent of the history read itself.
/// </summary>
public sealed class ControloHistoricoNavigationTests
{
    private const string HistoricoPath = "/controlo/approve/historico";

    /// <summary>
    /// HISTNAV-1 — with both Controlo grants the Histórico page renders the shared Controlo tab
    /// strip, marks Histórico as the current tab, links Resumo/Peso/Definições to their real
    /// routes, keeps Pegamentos as a stated unavailable state and never presents Comparação as a
    /// tab (the comparison workflow lives inside the Peso surfaces), in the contracted
    /// order, and the Resumo back-link carries the production identity through the existing
    /// Resumo direct-link contract.
    /// </summary>
    [Fact]
    public async Task HISTNAV1_HistoricoRendersSharedTabStripWithBothGrants()
    {
        using var factory = P2T06TestHost.ForUser(P2T06TestHost.BothGranted());
        using var client = factory.CreateClient();

        using var response = await P2T06TestHost.GetAsync(
            client,
            $"{HistoricoPath}?reference=REF-NAV&productionNumber=PROD-999&machine=M1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = System.Net.WebUtility.HtmlDecode(html);

        Assert.Contains("data-dmo-controlo-tabs=\"true\"", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\" data-dmo-controlo-tab=\"historico\"", decodedHtml, StringComparison.Ordinal);

        // The Resumo back-link preserves the production identity via the existing Resumo query contract.
        Assert.Contains("href=\"/controlo/resumo?ref=REF-NAV&production=PROD-999\"", decodedHtml, StringComparison.Ordinal);

        Assert.Contains("href=\"/controlo/create\"", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("data-dmo-controlo-tab=\"peso\"", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"/controlo/create/definicoes\"", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("data-dmo-controlo-tab=\"definicoes\"", decodedHtml, StringComparison.Ordinal);

        // Comparação is not a Controlo destination: no tab exists for it.
        Assert.DoesNotContain("data-dmo-controlo-tab=\"comparacao\"", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("data-dmo-controlo-tab=\"pegamentos\"", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("Os Pegamentos ainda não estão disponíveis neste build.", decodedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("/controlo/comparacao", decodedHtml, StringComparison.Ordinal);

        var resumo = decodedHtml.IndexOf("data-dmo-controlo-tab=\"resumo\"", StringComparison.Ordinal);
        var peso = decodedHtml.IndexOf("data-dmo-controlo-tab=\"peso\"", StringComparison.Ordinal);
        var pegamentos = decodedHtml.IndexOf("data-dmo-controlo-tab=\"pegamentos\"", StringComparison.Ordinal);
        var historico = decodedHtml.IndexOf("aria-current=\"page\" data-dmo-controlo-tab=\"historico\"", StringComparison.Ordinal);
        var definicoes = decodedHtml.IndexOf("data-dmo-controlo-tab=\"definicoes\"", StringComparison.Ordinal);

        Assert.True(resumo >= 0 && peso > resumo && pegamentos > peso && historico > pegamentos && definicoes > historico);
    }

    /// <summary>
    /// HISTNAV-2 — with only the Controlo Approve grant the create-side tabs (Resumo, Peso and
    /// Definições) are truthfully unavailable, Histórico remains the current tab, Pegamentos stays
    /// in its stated unavailable state, and no Comparação tab exists (it is not a destination).
    /// </summary>
    [Fact]
    public async Task HISTNAV2_HistoricoGatesCreateSideTabsForApproveOnly()
    {
        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted());
        using var client = factory.CreateClient();

        using var response = await P2T06TestHost.GetAsync(client, HistoricoPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = System.Net.WebUtility.HtmlDecode(html);

        Assert.Contains("aria-current=\"page\" data-dmo-controlo-tab=\"historico\"", decodedHtml, StringComparison.Ordinal);

        // The create-side tabs are present but marked unavailable; they never render as dead links.
        Assert.Contains("data-dmo-controlo-tab=\"resumo\"", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("data-dmo-controlo-tab-unavailable=\"O Resumo requer a concessão Controlo Create.\"", decodedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/controlo/resumo\"", decodedHtml, StringComparison.Ordinal);

        Assert.Contains("data-dmo-controlo-tab=\"peso\"", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("data-dmo-controlo-tab-unavailable=\"O Peso requer a concessão Controlo Create.\"", decodedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/controlo/create\"", decodedHtml, StringComparison.Ordinal);

        Assert.Contains("data-dmo-controlo-tab=\"definicoes\"", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("data-dmo-controlo-tab-unavailable=\"As Definições requerem a concessão Controlo Create.\"", decodedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/controlo/create/definicoes\"", decodedHtml, StringComparison.Ordinal);

        // Comparação is not a Controlo destination: no tab exists for it.
        Assert.DoesNotContain("data-dmo-controlo-tab=\"comparacao\"", decodedHtml, StringComparison.Ordinal);

        Assert.Contains("data-dmo-controlo-tab=\"pegamentos\"", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("Os Pegamentos ainda não estão disponíveis neste build.", decodedHtml, StringComparison.Ordinal);
    }

    /// <summary>
    /// HISTNAV-3 — the standalone <c>/controlo/comparacao</c> route from the interrupted K3 attempt
    /// no longer exists. Comparação is not a Controlo destination: the workflow only exists inside
    /// the existing Peso Create/Approve surfaces.
    /// </summary>
    [Fact]
    public async Task HISTNAV3_NoStandaloneComparisonRouteRemains()
    {
        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted());
        using var client = factory.CreateClient();

        using var response = await P2T06TestHost.GetAsync(client, "/controlo/comparacao");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

