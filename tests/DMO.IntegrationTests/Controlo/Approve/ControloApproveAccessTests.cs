using System.Net;
using DMO.Application.Access;

namespace DMO.IntegrationTests.Controlo.Approve;

/// <summary>
/// P2-T06 integration — ACCESS (contract §26.4 rows A1–A4/A6, AC-A1…AC-A4): every one of the nine
/// P2-T06 routes carries exactly <c>dmo.module.controlo-approve</c>; Create never grants Approve
/// and Approve never grants Create (incl. every Definições route), the shared <c>controlo</c>
/// destination never merges the grants, ADMIN gains nothing, and denial is never an empty
/// list/blank surface.
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract §14, <c>ACCESS_AND_NAVIGATION.md</c>, master plan §7 P2-T06 access
/// requirements. The two pages (routes 1–2) and the seven endpoints (routes 3–9) of §13.2 are
/// each probed with a denied caller (direct-route server-side denial — hiding is never
/// authorization).</remarks>
public sealed class ControloApproveAccessTests
{
    /// <summary>All nine P2-T06 routes (contract §13.2).</summary>
    private static readonly (string Path, bool RequiresBody)[] AllNineRoutes =
    [
        ("/controlo/approve", false),
        ("/controlo/approve/historico", false),
        ("/controlo/approve/pending", false),
        ("/controlo/approve/history", false),
        ("/controlo/approve/pesos/00000000-0000-0000-0000-000000000001", false),
        ("/controlo/approve/pesos/00000000-0000-0000-0000-000000000001/decisions", false),
        ("/controlo/approve/pesos/00000000-0000-0000-0000-000000000001/approve", true),
        ("/controlo/approve/pesos/00000000-0000-0000-0000-000000000001/reject", true),
        ("/controlo/approve/pesos/00000000-0000-0000-0000-000000000001/reopen", true),
    ];

    /// <summary>The Core P2-T05 routes a Create-only caller MAY reach (positive control) and an
    /// Approve-only caller must NOT reach (AC-A3) — a representative subset incl. Definições; the
    /// repairer surface row points at the MOVED gate (<c>Boquilhas > Definições</c>, §34.3 — an
    /// approve-only caller is denied there too, server-side).</summary>
    private static readonly (string Path, bool RequiresBody)[] CreateRoutes =
    [
        ("/controlo/create", false),
        ("/controlo/create/definicoes", false),
        ("/controlo/create/pesos", true),
        ("/boquilhas/definicoes/repairers", false),
        ("/controlo/create/definicoes/glass-densities", false),
    ];

    /// <summary>
    /// A1 (AC-A2): a <c>controlo-approve</c>-granted caller reaches every one of the nine routes
    /// (200/400/404 are all "reached" server-side outcomes; 403 is the only denial).
    /// </summary>
    [Fact]
    public async Task A1_ApproveGrantedCallerReachesAllNineRoutes()
    {
        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted());
        using var client = factory.CreateClient();

        foreach (var (path, requiresBody) in AllNineRoutes)
        {
            using var response = requiresBody
                ? await P2T06TestHost.SendJsonAsync(
                    client,
                    HttpMethod.Post,
                    path,
                    P2T06TestHost.Json(new { expectedVersion = 1, reason = "motivo" }))
                : await P2T06TestHost.GetAsync(client, path);

            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    /// <summary>
    /// A1 (AC-A2): a <c>controlo-create</c>-ONLY caller is denied EVERY one of the nine routes
    /// — direct-route server-side denial, never by hiding controls.
    /// </summary>
    [Fact]
    public async Task A1_CreateOnlyCallerIsDeniedEveryApproveRoute()
    {
        using var factory = P2T06TestHost.ForUser(P2T06TestHost.CreateOnly());
        using var client = factory.CreateClient();

        foreach (var (path, requiresBody) in AllNineRoutes)
        {
            using var response = requiresBody
                ? await P2T06TestHost.SendJsonAsync(
                    client,
                    HttpMethod.Post,
                    path,
                    P2T06TestHost.Json(new { expectedVersion = 1, reason = "motivo" }))
                : await P2T06TestHost.GetAsync(client, path);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    /// <summary>
    /// A2 (AC-A3): a <c>controlo-approve</c>-ONLY caller is denied every P2-T05 route — incl. the
    /// Create page, the Definições page/surfaces and the Peso mutation endpoints (server-side).
    /// </summary>
    [Fact]
    public async Task A2_ApproveOnlyCallerIsDeniedEveryCreateAndDefinicoesRoute()
    {
        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted());
        using var client = factory.CreateClient();

        foreach (var (path, requiresBody) in CreateRoutes)
        {
            using var response = requiresBody
                ? await P2T06TestHost.SendJsonAsync(
                    client,
                    HttpMethod.Post,
                    path,
                    P2T06TestHost.Json(new { }))
                : await P2T06TestHost.GetAsync(client, path);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    /// <summary>
    /// A3 (AC-A2/AC-A4): with BOTH grants the shared <c>controlo</c> destination is visible to
    /// both modules, but the route-level gates stay fully separate — a Create-granted caller can
    /// still not reach an Approve route and vice versa.
    /// </summary>
    [Fact]
    public async Task A3_BothGrantsKeepTheRouteLevelGatesFullySeparate()
    {
        using var factoryCreate = P2T06TestHost.ForUser(P2T06TestHost.CreateOnly());
        using var clientCreate = factoryCreate.CreateClient();

        using (var denied = await P2T06TestHost.GetAsync(clientCreate, "/controlo/approve"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using var factoryApprove = P2T06TestHost.ForUser(P2T06TestHost.AllGranted());
        using var clientApprove = factoryApprove.CreateClient();

        using (var denied = await P2T06TestHost.GetAsync(clientApprove, "/controlo/create"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
    }

    /// <summary>
    /// A4 (AC-A4): ADMIN is denied every P2-T06 route (no implicit operational access; the
    /// module gate fails closed for non-USER accounts).
    /// </summary>
    [Fact]
    public async Task A4_AdminIsDeniedEveryApproveRoute()
    {
        using var factory = P2T06TestHost.ForAdmin(P2T06TestHost.AllGranted());
        using var client = factory.CreateClient();

        foreach (var (path, requiresBody) in AllNineRoutes)
        {
            using var response = requiresBody
                ? await P2T06TestHost.SendJsonAsync(
                    client,
                    HttpMethod.Post,
                    path,
                    P2T06TestHost.Json(new { expectedVersion = 1, reason = "motivo" }))
                : await P2T06TestHost.GetAsync(client, path);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    /// <summary>
    /// A6 (AC-A4): denial is server-side and direct-route — a create-only caller receives 403 on
    /// EVERY P2-T06 route (a denied caller never gets a served surface, never an empty list
    /// replacing the denial), and the GRANTED surface renders its real regions (the freeze
    /// discipline: denial is never a blank surface — the denied caller gets no surface at all).
    /// </summary>
    [Fact]
    public async Task A6_DenialIsServerSideAndNeverABlankSurface()
    {
        using var deniedFactory = P2T06TestHost.ForUser(P2T06TestHost.CreateOnly());
        using var deniedClient = deniedFactory.CreateClient();

        foreach (var (path, requiresBody) in AllNineRoutes)
        {
            using var response = requiresBody
                ? await P2T06TestHost.SendJsonAsync(
                    deniedClient,
                    HttpMethod.Post,
                    path,
                    P2T06TestHost.Json(new { expectedVersion = 1, reason = "motivo" }))
                : await P2T06TestHost.GetAsync(deniedClient, path);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // The granted surface renders real content — never blank.
        using var grantedFactory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted());
        using var grantedClient = grantedFactory.CreateClient();
        using var granted = await P2T06TestHost.GetAsync(grantedClient, "/controlo/approve");
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.Contains(
            "data-dmo-approve-surface",
            await granted.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }
}