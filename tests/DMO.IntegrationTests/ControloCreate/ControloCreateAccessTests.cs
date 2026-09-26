using System.Net;
using System.Text.RegularExpressions;
using DMO.Application.Access;
using DMO.Application.Controlo.Pesos;
using DMO.IntegrationTests.Navigation;
using DMO.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DMO.IntegrationTests.ControloCreate;

/// <summary>
/// P2-T05 route/policy/access proofs: every P2-T05 route carries the single canonical
/// <c>dmo.module.controlo-create</c> policy, the shared <c>controlo</c> destination never merges
/// grants, an ADMIN session gains nothing operational, and no <c>controlo-approve</c> surface or
/// route exists for a create-only holder.
/// </summary>
/// <remarks>
/// <para>
/// Authority: P2-T05 contract §21.3 (routes 1–17), §21.5 (the Definições gating proof), §26.4 rows
/// AUT1–AUT4 (AC-G1–AC-G4). The endpoint inventory is the REAL application's
/// <see cref="EndpointDataSource"/>; callers are marked authenticated by the test scheme, so a
/// policy denial surfaces as 403 and never as a 401 challenge.</para>
/// <para>
/// Bodies are present but irrelevant: the gate denies first, so no request ever reaches a binder or
/// a service. A denied write never writes anything — the compositions' counts stay at their seeded
/// baseline.</para>
/// </remarks>
public sealed class ControloCreateAccessTests
{
    private const string CreatePage = "/controlo/create";
    private const string DefinicoesPage = "/controlo/create/definicoes";

    /// <summary>The canonical policy of <c>Controlo Create</c> (derived from the Module catalog).</summary>
    private static readonly string CreatePolicy =
        ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.ControloCreate);

    /// <summary>The canonical policy of <c>Controlo Approve</c> (derived from the Module catalog).</summary>
    private static readonly string ApprovePolicy =
        ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.ControloApprove);

    /// <summary>
    /// AUT1 (contract §26.4) — proves AC-G1: a caller granted NOTHING is denied EVERY P2-T05 route
    /// with 403 — both pages, every route 2–11 and every Definições minimal-API route — and no
    /// denial writes anything.
    /// </summary>
    [Fact]
    public async Task AUT1_ACallerWithNoGrantsIsDeniedEveryP2T05Route()
    {
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser(Array.Empty<ModuleDefinition>(), composition);
        using var client = factory.CreateClient();

        foreach (var route in AllP2T05Routes())
        {
            using var response = await SendAsync(client, route);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                response.StatusCode);
        }

        // The denials reached no service and wrote nothing.
        Assert.Equal(0, composition.PesoCount);
        Assert.Equal(0, composition.JobOnToolStore.ToolCount);
        Assert.Equal(0, composition.JobOnToolStore.JobOnCount);
    }

    /// <summary>
    /// AUT2 (contract §26.4) — proves AC-G2: a caller granted ONLY <c>controlo-approve</c> is
    /// denied EVERY P2-T05 surface with 403 — the shared <c>controlo</c> destination never merges
    /// grants, so Definições (and the whole Create surface) stays denied server-side.
    /// </summary>
    [Fact]
    public async Task AUT2_AnApproveOnlyCallerIsDeniedEveryP2T05Surface()
    {
        var approve = TestNavigationComposition.Definition(
            ModuleCatalog.ControloApprove, "Controlo Approve", "controlo", "Controlo");
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser([approve], composition);
        using var client = factory.CreateClient();

        // The Definições gating proof (§21.5): the approve grant sits on the SAME destination id as
        // controlo-create, yet both pages are denied.
        using (var create = await P2T05TestHost.GetAsync(client, CreatePage))
        {
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        }

        using (var definicoes = await P2T05TestHost.GetAsync(client, DefinicoesPage))
        {
            Assert.Equal(HttpStatusCode.Forbidden, definicoes.StatusCode);
        }

        foreach (var route in AllP2T05Routes())
        {
            using var response = await SendAsync(client, route);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal(0, composition.PesoCount);
    }

    /// <summary>
    /// AUT3 (contract §26.4) — proves AC-G3: an ADMIN session receives 403 on every spot-checked
    /// P2-T05 route (both pages, calculate, a Definições read and a Definições write), however the
    /// Modules are granted, and nothing is written.
    /// </summary>
    [Fact]
    public async Task AUT3_AnAdminSessionReceivesForbiddenOnEverySpotCheckedP2T05Route()
    {
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForAdmin(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using (var create = await P2T05TestHost.GetAsync(client, CreatePage))
        {
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        }

        using (var definicoes = await P2T05TestHost.GetAsync(client, DefinicoesPage))
        {
            Assert.Equal(HttpStatusCode.Forbidden, definicoes.StatusCode);
        }

        using (var calculate = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{CreatePage}/calculate",
                   P2T05TestHost.Json(new
                   {
                       pendingToolId = Guid.NewGuid(),
                       waterTemperature = 20m,
                       rows = new object[] { new { waterWeightG = 2500.5m } },
                   })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, calculate.StatusCode);
        }

        // The repairer surface MOVED to Boquilhas (Owner clarification P2-T07 §34.3): the
        // Controlo repairer/assignment routes no longer exist under Controlo at all — the
        // moved gate is proven by the Boquilhas access rows.

        // ADMIN gained nothing operational and wrote nothing.
        Assert.Equal(0, composition.PesoCount);
        Assert.Equal(0, composition.JobOnToolStore.ToolCount);
    }

    /// <summary>
    /// AUT4 (contract §26.4) — proves AC-G4: with ONLY the create surface granted, the
    /// <c>controlo-approve</c> surface exists as a SEPARATE sibling (P2-T06 disclosed extension:
    /// the P2-T06 implementation added the approve surface, so the P2-T05-time "no approve route
    /// exists" posture is superseded by the sibling-non-satisfaction rule) — no route pattern
    /// under <c>/controlo/approve</c> is ever served to a create-only holder, no P2-T05 route
    /// metadata carries the approve policy, and a direct GET of <c>/controlo/approve</c> is a
    /// server-side 403 — never served-as-create, never redirected into the create surface.
    /// </summary>
    [Fact]
    public async Task AUT4_CreateOnlyHolderHasNoApproveSurfaceAtAll()
    {
        Assert.Equal("dmo.module.controlo-approve", ApprovePolicy);
        Assert.NotEqual(ApprovePolicy, CreatePolicy);

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), new P2T05TestComposition());
        var endpoints = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .ToArray();

        // The approve surface EXISTS now (P2-T06 disclosed extension) and is never a create route.
        Assert.Contains(
            endpoints,
            endpoint => NormalizePath(endpoint.RoutePattern.RawText ?? string.Empty)
                .StartsWith("/controlo/approve", StringComparison.Ordinal));

        // Every mapped P2-T05 route carries EXACTLY the canonical controlo-create policy — never
        // the approve policy and never a blank policy.
        foreach (var endpoint in endpoints.Where(endpoint =>
                     NormalizePath(endpoint.RoutePattern.RawText ?? string.Empty)
                         .StartsWith("/controlo/create", StringComparison.Ordinal)))
        {
            var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ToArray();

            Assert.True(
                authorizeData.Length >= 1,
                $"A P2-T05 endpoint is mapped without IAuthorizeData: "
                + $"{NormalizePath(endpoint.RoutePattern.RawText ?? string.Empty)}.");

            Assert.All(authorizeData, data => Assert.Equal(CreatePolicy, data.Policy));
        }

        // The direct route is denied server-side for a create-only holder: 403, never served.
        using var client = factory.CreateClient();
        using var approve = await P2T05TestHost.GetAsync(client, "/controlo/approve");
        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
    }

    // ---- inventory helpers -------------------------------------------------------------

    /// <summary>One requested route: verb, path (with any query) and optional JSON body.</summary>
    private sealed record RouteCall(HttpMethod Method, string Path, string? Body);

    /// <summary>
    /// EVERY P2-T05 surface route: the two pages, routes 2–11 and every Definições minimal-API
    /// route (13–17, GET/POST/PUT/DELETE variants). Bodies are present-but-irrelevant JSON: the gate
    /// denies before any binder runs.
    /// </summary>
    private static IReadOnlyList<RouteCall> AllP2T05Routes()
    {
        Guid id = Guid.NewGuid();
        Guid toolId = Guid.NewGuid();

        return
        [
            new(HttpMethod.Get, CreatePage, null),
            new(HttpMethod.Get, DefinicoesPage, null),

            new(HttpMethod.Get, $"{CreatePage}/productions?reference=ACCESS-TEST", null),
            new(HttpMethod.Get, $"{CreatePage}/jobons/{id}", null),
            new(HttpMethod.Get, $"{CreatePage}/tools/{toolId}/association-candidates", null),

            new(HttpMethod.Post, $"{CreatePage}/calculate",
                P2T05TestHost.Json(new
                {
                    pendingToolId = toolId,
                    waterTemperature = 20m,
                    rows = new object[] { new { waterWeightG = 2500.5m } },
                })),
            new(HttpMethod.Post, $"{CreatePage}/pesos",
                P2T05TestHost.Json(new
                {
                    pendingToolId = toolId,
                    waterTemperature = 20m,
                    rows = new object[] { new { waterWeightG = 2500.5m } },
                })),
            new(HttpMethod.Put, $"{CreatePage}/pesos/{id}",
                P2T05TestHost.Json(new
                {
                    expectedVersion = 1,
                    waterTemperature = 20m,
                    rows = new object[] { new { waterWeightG = 2500.5m } },
                })),
            new(HttpMethod.Post, $"{CreatePage}/pesos/{id}/submit",
                P2T05TestHost.Json(new { expectedVersion = 1 })),
            new(HttpMethod.Post, $"{CreatePage}/pesos/{id}/associate",
                P2T05TestHost.Json(new { cmId = Guid.NewGuid(), expectedVersion = 1 })),
            new(HttpMethod.Post, $"{CreatePage}/jobons/{id}/cm-association",
                P2T05TestHost.Json(new { toolId, expectedJobOnVersion = 1 })),

            // Definições route 15 — PDF directory.
            new(HttpMethod.Get, $"{DefinicoesPage}/pdf-directory", null),
            new(HttpMethod.Put, $"{DefinicoesPage}/pdf-directory",
                P2T05TestHost.Json(new { baseDirectory = "C:\\reports", expectedVersion = (int?)null })),
            new(HttpMethod.Post, $"{DefinicoesPage}/pdf-directory/check", null),

            // Definições route 16 — email lists.
            new(HttpMethod.Get, $"{DefinicoesPage}/email-lists", null),
            new(HttpMethod.Post, $"{DefinicoesPage}/email-lists",
                P2T05TestHost.Json(new { name = "Fábrica", recipients = new[] { "a@x.pt" } })),
            new(HttpMethod.Get, $"{DefinicoesPage}/email-lists/{id}", null),
            new(HttpMethod.Put, $"{DefinicoesPage}/email-lists/{id}",
                P2T05TestHost.Json(new { expectedVersion = 1, name = "Fábrica", recipients = new[] { "a@x.pt" } })),
            new(HttpMethod.Delete, $"{DefinicoesPage}/email-lists/{id}?expectedVersion=1&deleteConfirmed=true", null),

            // Definições route 17 — email templates.
            new(HttpMethod.Get, $"{DefinicoesPage}/email-templates", null),
            new(HttpMethod.Post, $"{DefinicoesPage}/email-templates",
                P2T05TestHost.Json(new { name = "Peso", subject = "Relatório", body = "Em anexo", documentType = "peso" })),
            new(HttpMethod.Get, $"{DefinicoesPage}/email-templates/{id}", null),
            new(HttpMethod.Put, $"{DefinicoesPage}/email-templates/{id}",
                P2T05TestHost.Json(new { expectedVersion = 1, name = "Peso", subject = "Relatório", body = "Em anexo", documentType = "peso" })),
            new(HttpMethod.Delete, $"{DefinicoesPage}/email-templates/{id}?expectedVersion=1&deleteConfirmed=true", null),

            // Definições routes 18/19 — glass densities (post-closure correction §5.3).
            new(HttpMethod.Get, $"{DefinicoesPage}/glass-densities", null),
            new(HttpMethod.Put, $"{DefinicoesPage}/glass-densities/NNPB",
                P2T05TestHost.Json(new { densityGcm3 = 2.5m, expectedVersion = 1 })),
        ];
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, RouteCall route) =>
        route.Body is null
            ? await P2T05TestHost.SendAuthenticatedAsync(
                client, new HttpRequestMessage(route.Method, route.Path))
            : await P2T05TestHost.SendJsonAsync(client, route.Method, route.Path, route.Body);

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
}