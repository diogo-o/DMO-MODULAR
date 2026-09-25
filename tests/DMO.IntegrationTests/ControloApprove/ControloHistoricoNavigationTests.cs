using System.Net;
using Xunit;

namespace DMO.IntegrationTests.ControloApprove;

/// <summary>
/// Histórico navigation slice: the real P2-T06 Histórico de Pesos page joined the secondary
/// Controlo navigation the Resumo landing page established. Proves the page renders the shared
/// tab strip with Histórico current, the Resumo tab links back to /controlo/resumo preserving the
/// production context through the pages' existing query contracts, Peso and Definições keep their
/// real routes when the caller holds Controlo Create, Comparação and Pegamentos stay truthful
/// unavailable states, and the capability gates still hold. No history rows are seeded because
/// navigation is independent of the history read itself.
/// </summary>
public sealed class ControloHistoricoNavigationTests
{
    private const string HistoricoPath = "/controlo/approve/historico";

    /// <summary>
    /// HISTNAV-1 — with both Controlo grants the Histórico page renders the shared Controlo tab
    /// strip, marks Histórico as the current tab, links Resumo/Peso/Definições to their real
    /// routes, keeps Comparação and Pegamentos as stated unavailable states in the contracted
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

        Assert.Contains("data-dmo-controlo-tab=\"comparacao\"", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("data-dmo-controlo-tab=\"pegamentos\"", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("A Comparação ainda não está disponível neste build.", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("Os Pegamentos ainda não estão disponíveis neste build.", decodedHtml, StringComparison.Ordinal);

        var resumo = decodedHtml.IndexOf("data-dmo-controlo-tab=\"resumo\"", StringComparison.Ordinal);
        var peso = decodedHtml.IndexOf("data-dmo-controlo-tab=\"peso\"", StringComparison.Ordinal);
        var comparacao = decodedHtml.IndexOf("data-dmo-controlo-tab=\"comparacao\"", StringComparison.Ordinal);
        var pegamentos = decodedHtml.IndexOf("data-dmo-controlo-tab=\"pegamentos\"", StringComparison.Ordinal);
        var historico = decodedHtml.IndexOf("aria-current=\"page\" data-dmo-controlo-tab=\"historico\"", StringComparison.Ordinal);
        var definicoes = decodedHtml.IndexOf("data-dmo-controlo-tab=\"definicoes\"", StringComparison.Ordinal);

        Assert.True(resumo >= 0 && peso > resumo && comparacao > peso && pegamentos > comparacao && historico > pegamentos && definicoes > historico);
    }

    /// <summary>
    /// HISTNAV-2 — with only the Controlo Approve grant the create-side tabs (Resumo, Peso and
    /// Definições) are truthfully unavailable, Histórico remains the current tab, and Comparação
    /// and Pegamentos stay in their stated unavailable state.
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

        Assert.Contains("data-dmo-controlo-tab=\"comparacao\"", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("data-dmo-controlo-tab=\"pegamentos\"", decodedHtml, StringComparison.Ordinal);
    }
}

