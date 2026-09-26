using System.Net;
using DMO.IntegrationTests.Host;
using DMO.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DMO.IntegrationTests;

/// <summary>
/// P1-T05 host smoke test — the ADMIN-only USER administration surface is actually mapped
/// (minimal-API routes + Razor Pages) and every route carries the single
/// <c>dmo.administration</c> policy; an anonymous request is never served.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class AdministrationEndpointTests : IClassFixture<DmoWebApplicationFactory>
{
    private readonly DmoWebApplicationFactory _factory;

    public AdministrationEndpointTests(DmoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void MinimalApiRoutes_AreRegistered_WithTheAdministrationPolicy()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        var expectedRelativePaths = new[]
        {
            "/administration/users",
            "/administration/users/templates",
            "/administration/users/{userId:guid}",
            "/administration/users/{userId:guid}/activate",
            "/administration/users/{userId:guid}/deactivate",
            "/administration/users/{userId:guid}/template",
            "/administration/users/{userId:guid}/reset-password",
            "/administration/users/{userId:guid}/resend-invite",
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
    public void RazorPages_ForTheAdministrationSurface_AreMapped()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        foreach (var page in new[] { "List", "Create", "Edit", "Delete", "ResetPassword", "ResendInvite" })
        {
            var mapped = endpoints
                .Where(endpoint => endpoint is RouteEndpoint route
                    && route.RoutePattern.RawText?.Contains($"Administration/Users/{page}", StringComparison.OrdinalIgnoreCase) == true)
                .ToArray();

            Assert.True(
                mapped.Length >= 1,
                $"Expected the Razor page 'Administration/Users/{page}' to be mapped, found none.");

            var authorization = mapped[0].Metadata.GetMetadata<IAuthorizeData>();
            Assert.NotNull(authorization);
            Assert.Equal(
                AdministrationAuthorizationPolicies.PolicyName,
                authorization!.Policy);
        }
    }

    [Fact]
    public async Task AnonymousRequest_ToAdministrationSurface_IsNeverServed()
    {
        // No session: the gate must reject before any business code runs (challenge redirect
        // or forbidden), and the surface must never answer 200 to an anonymous caller.
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync(DMO.Web.Endpoints.Administration.UserAdministrationEndpoints.UsersBasePath);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            response.StatusCode,
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.Redirect });
    }
}