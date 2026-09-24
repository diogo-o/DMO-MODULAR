using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DMO.Application.Access;
using DMO.Domain.JobOn;
using DMO.Domain.Tools;
using DMO.IntegrationTests.Host;
using DMO.IntegrationTests.Navigation;
using DMO.Web.Authorization;
using DMO.Web.Pages.Ferramentas;
using DMO.Web.Pages.JobOn;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DMO.IntegrationTests.JobOn;

/// <summary>
/// P2-T04 route/policy/access proofs: the contracted routes (the 14 of §13.2 plus the additive
/// outputs-slice open route), their single canonical Module policy, and the HTTP boundary decision
/// for every caller state.
/// </summary>
/// <remarks>
/// <para>
/// Authority: P2-T04 contract §13.2 (the complete route table), §13.4 (policy ownership), §13.5
/// (interim availability), §14.3 and §20.6 rows RTE1–RTE10, RTE13, RTE16, RTE17 and RTE18.
/// </para>
/// <para>
/// The endpoint inventory is the REAL application's <see cref="EndpointDataSource"/>: minimal-API
/// routes carry <c>RequireAuthorization</c> and Razor Pages carry their <c>[Authorize]</c> attribute,
/// so both are read as endpoint metadata exactly like the accepted administration-endpoint precedent.
/// Callers are marked authenticated by the test scheme, so a policy denial surfaces as 403 and never as
/// a 401 challenge (the anonymous rows prove the challenge separately).
/// </para>
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class JobOnAccessTests
{
    private const string JobOnPage = "/jobon";
    private const string ProductionsApi = "/jobon/productions";
    private const string CreatePage = "/jobon/create";
    private const string CreateProductionsApi = "/jobon/create/productions";
    private const string ToolsApi = "/ferramentas/tools";

    /// <summary>The canonical policy of <c>Job On View</c> (derived from the Module catalog).</summary>
    private static readonly string ViewPolicy =
        ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.JobOnView);

    /// <summary>The canonical policy of <c>Job On Create</c> (derived from the Module catalog).</summary>
    private static readonly string CreatePolicy =
        ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.JobOnCreate);

    /// <summary>The canonical policy of <c>Ferramentas</c> (derived from the Module catalog).</summary>
    private static readonly string FerramentasPolicy =
        ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.Ferramentas);

    /// <summary>
    /// The 15 contracted P2-T04/Job On routes: the 14 of §13.2 plus the additive consult route of the
    /// outputs slice (open the Peso PDF of a related Peso — verb, normalized path template, contracted
    /// policy).
    /// </summary>
    private static readonly ContractedRoute[] ContractedRoutes =
    [
        new("GET", "/jobon", ViewPolicy, "route 1 — Job On consult page"),
        new("GET", "/jobon/productions", ViewPolicy, "route 2 — consult productions query"),
        new("GET", "/jobon/{}", ViewPolicy, "route 3 — Job On sheet page"),
        new("GET", "/jobon/create", CreatePolicy, "route 4 — Job On create page"),
        new("GET", "/jobon/create/productions", CreatePolicy, "route 5 — create-surface productions query"),
        new("POST", "/jobon", CreatePolicy, "route 6 — Job On create"),
        new("GET", "/jobon/{}/edit", CreatePolicy, "route 7 — Job On edit page"),
        new("PUT", "/jobon/{}", CreatePolicy, "route 8 — Job On edit"),
        new("GET", "/jobon/{}/duplicate", CreatePolicy, "route 9 — Job On duplication preview page"),
        new("POST", "/jobon/{}/duplicate", CreatePolicy, "route 10 — Job On duplication"),
        new("DELETE", "/jobon/{}", CreatePolicy, "route 11 — Job On delete"),
        new("GET", "/ferramentas/tools", FerramentasPolicy, "route 12 — canonical Tool search/list"),
        new("POST", "/ferramentas/tools", FerramentasPolicy, "route 13 — canonical Tool create"),
        new("GET", "/ferramentas/tools/{}", FerramentasPolicy, "route 14 — contextual Tool ficha page"),
        new("GET", "/jobon/{}/pesos/{}/peso-pdf", ViewPolicy, "route 15 — Peso PDF open (outputs slice, consult)"),
    ];

    /// <summary>The six contracted Job On/Ferramentas page models and their contracted policy.</summary>
    private static readonly (Type Model, string Policy, string Route)[] ContractedPageModels =
    [
        (typeof(IndexModel), ViewPolicy, "GET /jobon"),
        (typeof(ViewModel), ViewPolicy, "GET /jobon/{}"),
        (typeof(CreateModel), CreatePolicy, "GET /jobon/create"),
        (typeof(EditModel), CreatePolicy, "GET /jobon/{}/edit"),
        (typeof(DuplicateModel), CreatePolicy, "GET /jobon/{}/duplicate"),
        (typeof(ToolModel), FerramentasPolicy, "GET /ferramentas/tools/{}"),
    ];

    /// <summary>RTE1 (contract §20.6) — proves AC-83: every mapped P2-T04 endpoint carries EXACTLY one
    /// policy, and that policy is the canonical generated policy of one of the three contracted
    /// Modules.</summary>
    [Fact]
    public void RTE1_EveryMappedEndpointCarriesExactlyOneCanonicalModulePolicy()
    {
        using var factory = P2T04TestHost.ForUser(P2T04TestHost.AllGranted(), new P2T04TestStore());
        var endpoints = P2T04Endpoints(factory);

        Assert.NotEmpty(endpoints);

        foreach (var endpoint in endpoints)
        {
            var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

            var single = Assert.Single(authorizeData);
            Assert.NotNull(single.Policy);
            Assert.Contains(single.Policy!, new[] { ViewPolicy, CreatePolicy, FerramentasPolicy });
        }
    }

    /// <summary>RTE2 (contract §20.6) — proves AC-83: exactly the 14 contracted routes exist, each with
    /// its contracted path, verb and single policy.</summary>
    [Fact]
    public void RTE2_TheFourteenContractedRoutesExistWithTheirContractedPolicy()
    {
        using var factory = P2T04TestHost.ForUser(P2T04TestHost.AllGranted(), new P2T04TestStore());
        var endpoints = P2T04Endpoints(factory);

        foreach (var route in ContractedRoutes)
        {
            var matches = endpoints
                .Where(endpoint => PathOf(endpoint) == route.Path && VerbsOf(endpoint).Contains(route.Verb))
                .ToArray();

            Assert.True(
                matches.Length == 1,
                $"Expected exactly one mapped endpoint for '{route.Verb} {route.Path}' ({route.Purpose}), found {matches.Length}.");

            var authorizeData = matches[0].Metadata.GetOrderedMetadata<IAuthorizeData>();
            var single = Assert.Single(authorizeData);
            Assert.Equal(route.Policy, single.Policy);
        }
    }

    /// <summary>RTE3 (contract §20.6) — proves AC-83: NO additional P2-T04 route, page or endpoint
    /// exists beyond the contracted surface (the 14 P2-T04 routes plus the additive outputs-slice
    /// open route), in the endpoint inventory and in the page-model list.</summary>
    [Fact]
    public void RTE3_NoAdditionalP2T04RouteOrPageExistsBeyondTheContractedSurface()
    {
        using var factory = P2T04TestHost.ForUser(P2T04TestHost.AllGranted(), new P2T04TestStore());
        var endpoints = P2T04Endpoints(factory);

        var mapped = endpoints.Select(endpoint => $"{VerbsOf(endpoint)[0]} {PathOf(endpoint)}").ToArray();
        var contracted = ContractedRoutes.Select(route => $"{route.Verb} {route.Path}").ToArray();

        Assert.Equal(15, endpoints.Count);
        Assert.Equal(contracted.Length, mapped.Length);
        Assert.Empty(mapped.Except(contracted, StringComparer.Ordinal));
        Assert.Empty(contracted.Except(mapped, StringComparer.Ordinal));

        // The page list is the same 14: six page models, each owning one contracted page route.
        Assert.Equal(6, ContractedPageModels.Length);
        Assert.Equal(
            ContractedPageModels.Length,
            ContractedPageModels.Select(page => page.Route).Distinct(StringComparer.Ordinal).Count());

        foreach (var page in ContractedPageModels)
        {
            Assert.Contains(page.Route, contracted);
        }

        Assert.Equal(
            6,
            endpoints.Count(endpoint => ContractedPageModels.Any(page => $"{VerbsOf(endpoint)[0]} {PathOf(endpoint)}" == page.Route)));
    }

    /// <summary>RTE4 (contract §20.6) — proves AC-84, AC-85 and AC-89: a caller holding ONLY
    /// <c>job-on-view</c> is served the three consult routes and denied every create route with 403,
    /// including the Create-only productions read of route 5.</summary>
    [Fact]
    public async Task RTE4_AViewOnlyCallerIsServedTheConsultRoutesAndDeniedEveryCreateRoute()
    {
        var store = new P2T04TestStore();
        var occurrence = store.SeedJobOn("REF-RTE4", "1000");
        var jobOnPath = $"/jobon/{occurrence.JobOnId.Value}";

        using var factory = P2T04TestHost.ForUser([ViewOnly()], store);
        using var client = factory.CreateClient();

        // Routes 1–3 are the consult surface: served.
        using (var route1 = await P2T04TestHost.GetAsync(client, JobOnPage))
        {
            Assert.Equal(HttpStatusCode.OK, route1.StatusCode);
        }

        using (var route2 = await P2T04TestHost.GetAsync(client, $"{ProductionsApi}?reference=REF-RTE4"))
        {
            Assert.Equal(HttpStatusCode.OK, route2.StatusCode);
        }

        using (var route3 = await P2T04TestHost.GetAsync(client, jobOnPath))
        {
            Assert.Equal(HttpStatusCode.OK, route3.StatusCode);
        }

        // Routes 4–11 deny: Job On View gains no create capability.
        using (var route4 = await P2T04TestHost.GetAsync(client, CreatePage))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route4.StatusCode);
        }

        using (var route5 = await P2T04TestHost.GetAsync(client, $"{CreateProductionsApi}?reference=REF-RTE4"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route5.StatusCode);
        }

        using (var route6 = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            JobOnPage,
            CreateJobOnBody("REF-RTE4", "1001")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route6.StatusCode);
        }

        using (var route7 = await P2T04TestHost.GetAsync(client, $"{jobOnPath}/edit"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route7.StatusCode);
        }

        using (var route8 = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Put,
            jobOnPath,
            UpdateJobOnBody(1, "REF-RTE4", "1000")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route8.StatusCode);
        }

        using (var route9 = await P2T04TestHost.GetAsync(client, $"{jobOnPath}/duplicate"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route9.StatusCode);
        }

        using (var route10 = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            $"{jobOnPath}/duplicate",
            DuplicateJobOnBody(1, "1001")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route10.StatusCode);
        }

        using (var route11 = await P2T04TestHost.SendAuthenticatedAsync(
            client,
            new HttpRequestMessage(
                HttpMethod.Delete,
                $"{jobOnPath}?expectedVersion=1&deleteConfirmed=true&dateThresholdAcknowledged=true")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route11.StatusCode);
        }

        // No denial wrote anything.
        Assert.Equal(1, store.JobOnCount);
        Assert.Equal(0, store.ContextCount);
    }

    /// <summary>RTE5 (contract §20.6) — proves AC-90: a caller holding ONLY <c>job-on-create</c>
    /// completes the whole create → edit → duplicate → delete flow, including the Create-only
    /// productions read of route 5, while the consult routes 1–3 stay denied.</summary>
    [Fact]
    public async Task RTE5_ACreateOnlyCallerCompletesTheFlowAndIsDeniedTheConsultRoutes()
    {
        var store = new P2T04TestStore();
        var tool = store.SeedTool(ToolType.Cm, "5447T173", "LOTE-RTE5");

        using var factory = P2T04TestHost.ForUser([CreateOnly()], store);
        using var client = factory.CreateClient();

        // Route 4.
        using (var route4 = await P2T04TestHost.GetAsync(client, CreatePage))
        {
            Assert.Equal(HttpStatusCode.OK, route4.StatusCode);
        }

        // Route 5 — the Create surface's own productions read.
        using (var route5 = await P2T04TestHost.GetAsync(client, $"{CreateProductionsApi}?reference=REF-RTE5"))
        {
            Assert.Equal(HttpStatusCode.OK, route5.StatusCode);

            var payload = await ReadJsonAsync(route5);
            Assert.Equal(0, payload.GetProperty("productions").GetArrayLength());
        }

        // Route 6 — create.
        Guid jobOnId;
        using (var route6 = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            JobOnPage,
            CreateJobOnBody("REF-RTE5", "1000", tool.ToolId.Value)))
        {
            Assert.Equal(HttpStatusCode.Created, route6.StatusCode);

            var payload = await ReadJsonAsync(route6);
            jobOnId = payload.GetProperty("jobonId").GetGuid();
            Assert.Equal(1, payload.GetProperty("version").GetInt32());
        }

        var jobOnPath = $"/jobon/{jobOnId}";
        Assert.Equal(1, store.JobOnCount);
        Assert.Equal(1, store.ContextCount);

        // Route 7.
        using (var route7 = await P2T04TestHost.GetAsync(client, $"{jobOnPath}/edit"))
        {
            Assert.Equal(HttpStatusCode.OK, route7.StatusCode);
        }

        // Route 8 — one committed edit increments the version exactly once.
        using (var route8 = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Put,
            jobOnPath,
            UpdateJobOnBody(1, "REF-RTE5", "1000")))
        {
            Assert.Equal(HttpStatusCode.OK, route8.StatusCode);

            var payload = await ReadJsonAsync(route8);
            Assert.Equal(jobOnId, payload.GetProperty("jobonId").GetGuid());
            Assert.Equal(2, payload.GetProperty("version").GetInt32());
        }

        // Route 9.
        using (var route9 = await P2T04TestHost.GetAsync(client, $"{jobOnPath}/duplicate"))
        {
            Assert.Equal(HttpStatusCode.OK, route9.StatusCode);
        }

        // Route 10 — duplication from the explicitly previewed source version.
        Guid duplicateId;
        using (var route10 = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            $"{jobOnPath}/duplicate",
            DuplicateJobOnBody(2, "1001")))
        {
            Assert.Equal(HttpStatusCode.Created, route10.StatusCode);

            var payload = await ReadJsonAsync(route10);
            duplicateId = payload.GetProperty("jobonId").GetGuid();
            Assert.Equal(jobOnId, payload.GetProperty("sourceJobOnId").GetGuid());
            Assert.NotEqual(jobOnId, duplicateId);
        }

        Assert.Equal(2, store.JobOnCount);

        // Route 11 — the duplicate first (it is not a duplication source), then the source.
        using (var deleteDuplicate = await P2T04TestHost.SendAuthenticatedAsync(
            client,
            DeleteRequest($"/jobon/{duplicateId}", expectedVersion: 1)))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleteDuplicate.StatusCode);
        }

        using (var deleteSource = await P2T04TestHost.SendAuthenticatedAsync(
            client,
            DeleteRequest(jobOnPath, expectedVersion: 2)))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleteSource.StatusCode);
        }

        Assert.Equal(0, store.JobOnCount);
        Assert.Equal(0, store.ContextCount);

        // The consult routes stay denied: the create grant never implies Job On View.
        using (var consultPage = await P2T04TestHost.GetAsync(client, JobOnPage))
        {
            Assert.Equal(HttpStatusCode.Forbidden, consultPage.StatusCode);
        }

        using (var consultQuery = await P2T04TestHost.GetAsync(client, $"{ProductionsApi}?reference=REF-RTE5"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, consultQuery.StatusCode);
        }

        using (var sheet = await P2T04TestHost.GetAsync(client, jobOnPath))
        {
            Assert.Equal(HttpStatusCode.Forbidden, sheet.StatusCode);
        }
    }

    /// <summary>RTE6 (contract §20.6) — proves AC-86: a caller holding ONLY <c>ferramentas</c> is
    /// served routes 12–14 and denied every Job On route.</summary>
    [Fact]
    public async Task RTE6_AFerramentasOnlyCallerIsServedTheToolRoutesAndDeniedEveryJobOnRoute()
    {
        var store = new P2T04TestStore();
        var tool = store.SeedTool(ToolType.Cm, "5447T173", "LOTE-RTE6");
        var occurrence = store.SeedJobOn("REF-RTE6", "1000");
        var jobOnPath = $"/jobon/{occurrence.JobOnId.Value}";

        using var factory = P2T04TestHost.ForUser([FerramentasOnly()], store);
        using var client = factory.CreateClient();

        // Route 12.
        using (var route12 = await P2T04TestHost.GetAsync(client, $"{ToolsApi}?reference=5447T173&limit=10"))
        {
            Assert.Equal(HttpStatusCode.OK, route12.StatusCode);
        }

        // Route 13.
        using (var route13 = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            ToolsApi,
            P2T04TestHost.Json(new
            {
                type = "CM",
                reference = "9999Z",
                lot = "LOTE-RTE6",
                machines = new[] { "B1" },
            })))
        {
            Assert.Equal(HttpStatusCode.Created, route13.StatusCode);
        }

        // Route 14.
        using (var route14 = await P2T04TestHost.GetAsync(client, $"{ToolsApi}/{tool.ToolId.Value}"))
        {
            Assert.Equal(HttpStatusCode.OK, route14.StatusCode);
        }

        // Every Job On route denies.
        using (var route1 = await P2T04TestHost.GetAsync(client, JobOnPage))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route1.StatusCode);
        }

        using (var route2 = await P2T04TestHost.GetAsync(client, $"{ProductionsApi}?reference=REF-RTE6"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route2.StatusCode);
        }

        using (var route3 = await P2T04TestHost.GetAsync(client, jobOnPath))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route3.StatusCode);
        }

        using (var route4 = await P2T04TestHost.GetAsync(client, CreatePage))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route4.StatusCode);
        }

        using (var route5 = await P2T04TestHost.GetAsync(client, $"{CreateProductionsApi}?reference=REF-RTE6"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route5.StatusCode);
        }

        using (var route6 = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            JobOnPage,
            CreateJobOnBody("REF-RTE6", "1001")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route6.StatusCode);
        }

        using (var route7 = await P2T04TestHost.GetAsync(client, $"{jobOnPath}/edit"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route7.StatusCode);
        }

        using (var route8 = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Put,
            jobOnPath,
            UpdateJobOnBody(1, "REF-RTE6", "1000")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route8.StatusCode);
        }

        using (var route9 = await P2T04TestHost.GetAsync(client, $"{jobOnPath}/duplicate"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route9.StatusCode);
        }

        using (var route10 = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            $"{jobOnPath}/duplicate",
            DuplicateJobOnBody(1, "1001")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route10.StatusCode);
        }

        using (var route11 = await P2T04TestHost.SendAuthenticatedAsync(
            client,
            DeleteRequest(jobOnPath, expectedVersion: 1)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route11.StatusCode);
        }

        Assert.Equal(1, store.JobOnCount);
        Assert.Equal(2, store.ToolCount);
    }

    /// <summary>RTE7 (contract §20.6) — proves AC-86: no P2-T04 route or page carries
    /// <c>dmo.module.ferramentas-approve</c>, while that canonical policy identity still exists
    /// unchanged in the generated policy set.</summary>
    [Fact]
    public void RTE7_NoP2T04RouteCarriesTheFerramentasApprovePolicy()
    {
        // The canonical identity is unchanged and still projected from the catalog: P2-T04 grants it
        // nothing and removes nothing.
        var approvePolicy = ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.FerramentasApprove);
        Assert.Equal("dmo.module.ferramentas-approve", approvePolicy);
        Assert.NotEqual(approvePolicy, FerramentasPolicy);

        using var factory = P2T04TestHost.ForUser(P2T04TestHost.AllGranted(), new P2T04TestStore());

        foreach (var endpoint in P2T04Endpoints(factory))
        {
            foreach (var authorizeData in endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>())
            {
                Assert.NotEqual(approvePolicy, authorizeData.Policy);
                Assert.Contains(authorizeData.Policy!, new[] { ViewPolicy, CreatePolicy, FerramentasPolicy });
            }
        }

        foreach (var page in ContractedPageModels)
        {
            var authorize = Assert.Single(page.Model.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
            Assert.NotEqual(approvePolicy, authorize.Policy);
        }
    }

    /// <summary>RTE8 (contract §20.6) — proves AC-87: an anonymous caller (no session mark at all) is
    /// never served a P2-T04 surface: every route answers 401/403 or a redirect, and never a 200
    /// carrying protected data.</summary>
    [Fact]
    public async Task RTE8_AnAnonymousCallerIsNeverServedAP2T04Surface()
    {
        var store = new P2T04TestStore();
        var tool = store.SeedTool(ToolType.Cm, "5447T173", "LOTE-RTE8");
        var occurrence = store.SeedJobOn("REF-RTE8", "1000");
        var jobOnPath = $"/jobon/{occurrence.JobOnId.Value}";

        using var factory = P2T04TestHost.ForAnonymous(P2T04TestHost.AllGranted(), store);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var paths = new[]
        {
            JobOnPage,
            $"{ProductionsApi}?reference=REF-RTE8",
            jobOnPath,
            CreatePage,
            $"{CreateProductionsApi}?reference=REF-RTE8",
            $"{jobOnPath}/edit",
            $"{jobOnPath}/duplicate",
            $"{ToolsApi}?reference=5447T173&limit=10",
            $"{ToolsApi}/{tool.ToolId.Value}",
        };

        foreach (var path in paths)
        {
            using var response = await client.GetAsync(path);
            var status = (int)response.StatusCode;

            Assert.True(
                status is 401 or 403 || status is >= 300 and < 400,
                $"An anonymous caller must be denied on '{path}'; observed {status}.");

            var html = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("data-dmo-jobon-surface", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-dmo-frozen-triple", html, StringComparison.Ordinal);
            Assert.DoesNotContain("REF-RTE8", html, StringComparison.Ordinal);
        }

        // The write routes are never served either.
        using (var post = await client.PostAsync(
            JobOnPage,
            new StringContent(CreateJobOnBody("REF-RTE8", "1001"), System.Text.Encoding.UTF8, "application/json")))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        }

        using (var delete = await client.DeleteAsync($"{jobOnPath}?expectedVersion=1&deleteConfirmed=true&dateThresholdAcknowledged=true"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
        }

        Assert.Equal(1, store.JobOnCount);
    }

    /// <summary>RTE9 (contract §20.6) — proves AC-88: an ADMIN session receives 403 on EVERY P2-T04
    /// route, however the Modules are granted.</summary>
    [Fact]
    public async Task RTE9_AnAdminSessionReceivesForbiddenOnEveryP2T04Route()
    {
        var store = new P2T04TestStore();
        var tool = store.SeedTool(ToolType.Cm, "5447T173", "LOTE-RTE9");
        var occurrence = store.SeedJobOn("REF-RTE9", "1000");
        var jobOnPath = $"/jobon/{occurrence.JobOnId.Value}";

        using var factory = P2T04TestHost.ForAdmin(P2T04TestHost.AllGranted(), store);
        using var client = factory.CreateClient();

        using (var route1 = await P2T04TestHost.GetAsync(client, JobOnPage))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route1.StatusCode);
        }

        using (var route2 = await P2T04TestHost.GetAsync(client, $"{ProductionsApi}?reference=REF-RTE9"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route2.StatusCode);
        }

        using (var route3 = await P2T04TestHost.GetAsync(client, jobOnPath))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route3.StatusCode);
        }

        using (var route4 = await P2T04TestHost.GetAsync(client, CreatePage))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route4.StatusCode);
        }

        using (var route5 = await P2T04TestHost.GetAsync(client, $"{CreateProductionsApi}?reference=REF-RTE9"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route5.StatusCode);
        }

        using (var route6 = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            JobOnPage,
            CreateJobOnBody("REF-RTE9", "1001")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route6.StatusCode);
        }

        using (var route7 = await P2T04TestHost.GetAsync(client, $"{jobOnPath}/edit"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route7.StatusCode);
        }

        using (var route8 = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Put,
            jobOnPath,
            UpdateJobOnBody(1, "REF-RTE9", "1000")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route8.StatusCode);
        }

        using (var route9 = await P2T04TestHost.GetAsync(client, $"{jobOnPath}/duplicate"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route9.StatusCode);
        }

        using (var route10 = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            $"{jobOnPath}/duplicate",
            DuplicateJobOnBody(1, "1001")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route10.StatusCode);
        }

        using (var route11 = await P2T04TestHost.SendAuthenticatedAsync(
            client,
            DeleteRequest(jobOnPath, expectedVersion: 1)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route11.StatusCode);
        }

        using (var route12 = await P2T04TestHost.GetAsync(client, $"{ToolsApi}?reference=5447T173&limit=10"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route12.StatusCode);
        }

        using (var route13 = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            ToolsApi,
            P2T04TestHost.Json(new
            {
                type = "CM",
                reference = "9999Z",
                lot = "LOTE-RTE9",
                machines = new[] { "B1" },
            })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route13.StatusCode);
        }

        using (var route14 = await P2T04TestHost.GetAsync(client, $"{ToolsApi}/{tool.ToolId.Value}"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, route14.StatusCode);
        }

        // ADMIN gained nothing operational and wrote nothing.
        Assert.Equal(1, store.JobOnCount);
        Assert.Equal(1, store.ToolCount);
    }

    /// <summary>RTE10 (contract §20.6) — proves AC-87: the Job On sheet for an unauthenticated caller
    /// renders no protected data at all (a denial, never a blank-but-rendered ficha).</summary>
    [Fact]
    public async Task RTE10_AnUnauthenticatedSheetRequestRendersNoProtectedData()
    {
        var store = new P2T04TestStore();
        var tool = store.SeedTool(ToolType.Cm, "5447T173", "LOTE-RTE10");
        var occurrence = store.SeedJobOn(
            "REF-RTE10",
            "1000",
            contexts:
            [
                new ToolContext(
                    ToolContextType.Cm,
                    Guid.NewGuid(),
                    JobOnId.New(),
                    tool.ToolId,
                    new ToolContextSnapshot(ToolType.Cm, "5447T173", "LOTE-RTE10")),
            ]);

        using var factory = P2T04TestHost.ForAnonymous(P2T04TestHost.AllGranted(), store);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync($"/jobon/{occurrence.JobOnId.Value}");

        var status = (int)response.StatusCode;
        Assert.True(
            status is 401 or 403 || status is >= 300 and < 400,
            $"An unauthenticated sheet request must be denied; observed {status}.");

        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("data-dmo-jobon-surface", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-dmo-frozen-triple", html, StringComparison.Ordinal);
        Assert.DoesNotContain("REF-RTE10", html, StringComparison.Ordinal);
        Assert.DoesNotContain("5447T173", html, StringComparison.Ordinal);
        Assert.DoesNotContain("LOTE-RTE10", html, StringComparison.Ordinal);
    }

    /// <summary>RTE13 (contract §20.6) — proves AC-94: in the PRODUCTION availability composition
    /// (<c>ModuleRegistrations.CurrentBuildAvailable</c> is empty) a user whose persisted Template
    /// grants <c>Job On Create</c> is still denied, because an unavailable Module fails the whole
    /// resolution closed.</summary>
    [Fact]
    public async Task RTE13_AnUnavailableModuleDeniesEvenAPersistedCreateGrant()
    {
        // The production availability register is untouched: P2-T04 registers no availability.
        Assert.Empty(ModuleRegistrations.CurrentBuildAvailable);

        var store = new P2T04TestStore();
        store.SeedJobOn("REF-RTE13", "1000");

        using var factory = P2T04TestHost.Create(
            TestNavigationComposition.User(),
            P2T04TestHost.AllGranted(),
            store,
            services =>
            {
                // Production-shaped composition: the registry knows exactly the (empty) build
                // availability, and the access outcome is the fail-closed "module unavailable"
                // denial the resolver produces for a persisted-but-unavailable Module.
                services.RemoveAll<IModuleRegistry>();
                services.AddSingleton<IModuleRegistry>(
                    ModuleRegistry.Create(ModuleRegistrations.CurrentBuildAvailable));

                services.RemoveAll<IModuleAccessService>();
                services.AddSingleton<IModuleAccessService>(
                    new TestNavigationComposition.FixedAccessService(
                        new AccessOutcome.Denied(AccessDenialReason.UnavailableModule)));
            });

        using var client = factory.CreateClient();

        using (var consult = await P2T04TestHost.GetAsync(client, JobOnPage))
        {
            Assert.Equal(HttpStatusCode.Forbidden, consult.StatusCode);
        }

        using (var create = await P2T04TestHost.GetAsync(client, CreatePage))
        {
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        }

        using (var createWrite = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            JobOnPage,
            CreateJobOnBody("REF-RTE13", "1001")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, createWrite.StatusCode);
        }

        using (var tools = await P2T04TestHost.GetAsync(client, $"{ToolsApi}?reference=5447T173&limit=10"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, tools.StatusCode);
        }

        Assert.Equal(1, store.JobOnCount);
    }

    /// <summary>RTE16 (contract §20.6) — proves AC-83: every P2-T04 page model carries exactly one
    /// <c>[Authorize]</c> whose policy is the canonical Module policy of its contracted route, and the
    /// pinned <c>JobOnPolicyNames</c> constants are provably that same canonical projection.</summary>
    [Fact]
    public void RTE16_EveryPageModelPolicysTheContractedCanonicalPolicy()
    {
        Assert.True(
            JobOnPolicyNames.MatchesCanonicalPolicies(),
            "The pinned JobOnPolicyNames constants must equal ModuleAuthorizationPolicies.PolicyName of their Module.");

        foreach (var page in ContractedPageModels)
        {
            var attributes = page.Model.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToArray();

            var authorize = Assert.Single(attributes);
            Assert.NotNull(authorize.Policy);
            Assert.Equal(page.Policy, authorize.Policy);
        }
    }

    /// <summary>RTE17 (contract §20.6) — proves AC-84: a mutating verb against the <c>job-on-view</c>
    /// productions route is not served successfully, and nothing is written.</summary>
    [Fact]
    public async Task RTE17_AMutatingVerbAgainstTheViewProductionsRouteIsNotServedSuccessfully()
    {
        var store = new P2T04TestStore();
        store.SeedJobOn("REF-RTE17", "1000");

        // The caller holds every P2-T04 grant, so any non-2xx here is method routing, not a denial.
        using var factory = P2T04TestHost.ForUser(P2T04TestHost.AllGranted(), store);
        using var client = factory.CreateClient();

        using (var put = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Put,
            ProductionsApi,
            P2T04TestHost.Json(new { reference = "REF-RTE17" })))
        {
            Assert.False(IsSuccess(put.StatusCode), $"PUT {ProductionsApi} must not be a success; observed {(int)put.StatusCode}.");
        }

        using (var delete = await P2T04TestHost.SendAuthenticatedAsync(
            client,
            new HttpRequestMessage(HttpMethod.Delete, ProductionsApi)))
        {
            Assert.False(IsSuccess(delete.StatusCode), $"DELETE {ProductionsApi} must not be a success; observed {(int)delete.StatusCode}.");
        }

        using (var post = await P2T04TestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            ProductionsApi,
            CreateJobOnBody("REF-RTE17", "1001")))
        {
            Assert.False(IsSuccess(post.StatusCode), $"POST {ProductionsApi} must not be a success; observed {(int)post.StatusCode}.");
        }

        // No mutating verb reached a write path.
        Assert.Equal(1, store.JobOnCount);
        Assert.Equal(0, store.ContextCount);
    }

    /// <summary>RTE18 (contract §20.6) — proves AC-83: EVERY mapped P2-T04 route carries
    /// <c>IAuthorizeData</c> metadata, so no P2-T04 route is mapped without authorization.</summary>
    [Fact]
    public void RTE18_EveryMappedRouteCarriesAuthorizeDataMetadata()
    {
        using var factory = P2T04TestHost.ForUser(P2T04TestHost.AllGranted(), new P2T04TestStore());
        var endpoints = P2T04Endpoints(factory);

        Assert.Equal(15, endpoints.Count);

        foreach (var endpoint in endpoints)
        {
            var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ToArray();

            Assert.True(
                authorizeData.Length >= 1,
                $"'{VerbsOf(endpoint)[0]} {PathOf(endpoint)}' is mapped without IAuthorizeData metadata.");

            Assert.All(
                authorizeData,
                data => Assert.False(
                    string.IsNullOrWhiteSpace(data.Policy),
                    $"'{VerbsOf(endpoint)[0]} {PathOf(endpoint)}' carries an authorize entry with no policy."));
        }
    }

    // ---- inventory helpers -----------------------------------------------------------------

    /// <summary>The mapped endpoints of the P2-T04 base paths, read from the real routing table.</summary>
    private static IReadOnlyList<RouteEndpoint> P2T04Endpoints(WebApplicationFactory<Program> factory) =>
        factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => IsP2T04Path(endpoint.RoutePattern.RawText))
            .ToArray();

    /// <summary>Whether a route pattern belongs to the P2-T04 base paths.</summary>
    private static bool IsP2T04Path(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        var normalized = NormalizePath(rawText);

        return normalized == "/jobon"
            || normalized.StartsWith("/jobon/", StringComparison.Ordinal)
            || normalized == "/ferramentas"
            || normalized.StartsWith("/ferramentas/", StringComparison.Ordinal);
    }

    /// <summary>The normalized path template of an endpoint (route parameters collapsed to <c>{}</c>).</summary>
    private static string PathOf(RouteEndpoint endpoint) =>
        NormalizePath(endpoint.RoutePattern.RawText ?? string.Empty);

    /// <summary>Collapses every route parameter to a single token, so parameter names never matter.</summary>
    private static string NormalizePath(string rawText)
    {
        var normalized = Regex.Replace(rawText, "\\{[^}]*\\}", "{}");

        // A Razor Page endpoint reports its raw text without a leading slash ("jobon"), while a
        // minimal-API endpoint reports it with one ("/jobon"); the contracted path vocabulary always
        // uses the leading-slash form.
        if (normalized.Length > 0 && normalized[0] != '/')
        {
            normalized = '/' + normalized;
        }

        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

    /// <summary>
    /// The HTTP verbs an endpoint answers. A Razor Page endpoint declares no method metadata: the page
    /// route serves GET (and HEAD, which is not a distinct P2-T04 route).
    /// </summary>
    private static IReadOnlyList<string> VerbsOf(RouteEndpoint endpoint)
    {
        var metadata = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>();
        if (metadata is null)
        {
            return [HttpMethods.Get];
        }

        var verbs = metadata.HttpMethods
            .Where(method => !string.Equals(method, HttpMethods.Head, StringComparison.Ordinal))
            .ToArray();

        return verbs.Length > 0 ? verbs : [HttpMethods.Get];
    }

    /// <summary>Whether a status is a 2xx success.</summary>
    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    // ---- arrangement helpers ---------------------------------------------------------------

    /// <summary>The Job On View grant alone.</summary>
    private static ModuleDefinition ViewOnly() =>
        P2T04TestHost.Definition(ModuleCatalog.JobOnView, "Job On View", "job-on", "Job On");

    /// <summary>The Job On Create grant alone.</summary>
    private static ModuleDefinition CreateOnly() =>
        P2T04TestHost.Definition(ModuleCatalog.JobOnCreate, "Job On Create", "job-on", "Job On");

    /// <summary>The contextual Ferramentas grant alone.</summary>
    private static ModuleDefinition FerramentasOnly() =>
        P2T04TestHost.Definition(ModuleCatalog.Ferramentas, "Ferramentas", null, "Ferramentas", contextual: true);

    /// <summary>A confirmed delete request with the stronger date acknowledgement.</summary>
    private static HttpRequestMessage DeleteRequest(string jobOnPath, int expectedVersion) =>
        new(
            HttpMethod.Delete,
            $"{jobOnPath}?expectedVersion={expectedVersion}&deleteConfirmed=true&dateThresholdAcknowledged=true");

    /// <summary>The Job On create transport body.</summary>
    private static string CreateJobOnBody(string reference, string productionNumber, Guid? cmToolId = null) =>
        P2T04TestHost.Json(new { reference, productionNumber, machine = "B1", cmToolId });

    /// <summary>The Job On edit transport body (facts + an explicit empty association change list).</summary>
    private static string UpdateJobOnBody(int expectedVersion, string reference, string productionNumber) =>
        P2T04TestHost.Json(new
        {
            expectedVersion,
            reference,
            productionNumber,
            machine = "B1",
            productionDate = (DateOnly?)null,
            associations = Array.Empty<object>(),
            dateThresholdWarningAcknowledged = true,
        });

    /// <summary>The Job On duplication transport body.</summary>
    private static string DuplicateJobOnBody(int expectedSourceVersion, string productionNumber) =>
        P2T04TestHost.Json(new
        {
            expectedSourceVersion,
            productionNumber,
            machine = "B1",
            productionDate = (DateOnly?)null,
        });

    /// <summary>Reads a JSON response body into a detached element.</summary>
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }

    /// <summary>One contracted P2-T04 route: verb, normalized path template and single policy.</summary>
    private sealed record ContractedRoute(string Verb, string Path, string Policy, string Purpose);
}
