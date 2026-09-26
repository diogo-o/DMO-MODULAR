using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DMO.IntegrationTests.Host;
using DMO.Web.Endpoints.Core;

namespace DMO.IntegrationTests;

/// <summary>
/// Proposed P1-T01 test — the technical startup endpoint answers with exactly the accepted
/// technical shape.
/// </summary>
/// <remarks>
/// PROPOSED — NOT EXECUTED. Awaiting Architect review before first execution.
/// See <c>docs/PROPOSED_TESTS_P1-T01.md</c> for the full test protocol record.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class TechnicalEndpointTests : IClassFixture<DmoWebApplicationFactory>
{
    /// <summary>
    /// The accepted P1-T01 technical response shape: exactly these fields, nothing else.
    /// </summary>
    /// <remarks>
    /// The endpoint contract is deliberately minimal. Asserting the exact field set is what
    /// prevents product/application data being added under an unanticipated name while the
    /// test still passes; a "does not contain X/Y/Z" assertion would not.
    /// </remarks>
    private static readonly string[] AcceptedFields = ["status", "environment"];

    private readonly DmoWebApplicationFactory _factory;

    public TechnicalEndpointTests(DmoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ReturnsOkWithExactlyTheAcceptedTechnicalShape()
    {
        // Preconditions: the host started with database configuration supplied.
        using var client = _factory.CreateClient();

        // Action: request the technical startup endpoint.
        var response = await client.GetAsync(TechnicalEndpoints.HealthPath);

        // Assertions: reachable and reports liveness.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        // The response is a JSON object with exactly the accepted technical fields.
        Assert.Equal(JsonValueKind.Object, payload.ValueKind);

        var actualFields = payload.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            AcceptedFields.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            actualFields);

        // The accepted fields carry the accepted values.
        Assert.Equal("ok", payload.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("environment").GetString()));

        Assert.Equal(2, payload.EnumerateObject().Count());
    }

    [Fact]
    public async Task Health_DoesNotExposeTheConnectionString()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(TechnicalEndpoints.HealthPath);
        var body = await response.Content.ReadAsStringAsync();

        // Required non-effect: no credential material leaks through the technical surface.
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dmo_placeholder", body, StringComparison.OrdinalIgnoreCase);
    }
}
