using System.Net;
using DMO.IntegrationTests.Host;
using DMO.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DMO.IntegrationTests.TemplateAdministration;

/// <summary>
/// P1-T06 host smoke test — the ADMIN-only Template administration surface is actually mapped
/// (minimal-API routes + Razor Pages) and every route carries the same single
/// <c>dmo.administration</c> policy as the P1-T05 USER surface; an anonymous request is never
/// served.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class TemplateAdministrationEndpointTests : IClassFixture<DmoWebApplicationFactory>
{
    private readonly DmoWebApplicationFactory _factory;

    public TemplateAdministrationEndpointTests(DmoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void MinimalApiRoutes_AreRegistered_WithTheAdministrationPolicy()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        var expectedRelativePaths = new[]
        {
            "/administration/templates",
            "/administration/templates/{templateId:guid}",
            "/administration/templates/{templateId:guid}/delete",
            "/administration/templates/{templateId:guid}/users/{userId:guid}",
            "/administration/templates/{toTemplateId:guid}/users/{userId:guid}/reassign",
        };

        foreach (var path in expectedRelativePaths)
        {
            var matches = endpoints
                .Where(endpoint => endpoint is RouteEndpoint route
                    && route.RoutePattern.RawText?.Contains(path, StringComparison.OrdinalIgnoreCase) == true)
                .ToArray();

            Assert.True(
                matches.Length >= 1,
                $"Expected a mapped route containing '{path}', found none.");

            foreach (var match in matches)
            {
                var authorization = match.Metadata.GetMetadata<IAuthorizeData>();
                Assert.NotNull(authorization);
                Assert.Equal(
                    AdministrationAuthorizationPolicies.PolicyName,
                    authorization!.Policy);
            }
        }
    }

    [Fact]
    public void RazorPages_ForTheTemplateAdministrationSurface_AreMapped()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        foreach (var page in new[] { "List", "Create", "Edit", "Delete" })
        {
            var mapped = endpoints
                .Where(endpoint => endpoint is RouteEndpoint route
                    && route.RoutePattern.RawText?.Contains($"Administration/Templates/{page}", StringComparison.OrdinalIgnoreCase) == true)
                .ToArray();

            Assert.True(
                mapped.Length >= 1,
                $"Expected the Razor page 'Administration/Templates/{page}' to be mapped, found none.");

            var authorization = mapped[0].Metadata.GetMetadata<IAuthorizeData>();
            Assert.NotNull(authorization);
            Assert.Equal(
                AdministrationAuthorizationPolicies.PolicyName,
                authorization!.Policy);
        }
    }

    [Fact]
    public async Task AnonymousRequest_ToTemplateAdministrationSurface_IsNeverServed()
    {
        // No session: the gate must reject before any business code runs (challenge redirect
        // or forbidden), and the surface must never answer 200 to an anonymous caller.
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync(DMO.Web.Endpoints.Administration.TemplateAdministrationEndpoints.TemplatesBasePath);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            response.StatusCode,
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.Redirect });
    }
}