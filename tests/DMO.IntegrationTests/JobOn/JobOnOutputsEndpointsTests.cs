using System.Net;
using System.Text.Json;
using DMO.Application.Access;
using DMO.Domain.JobOn;
using DMO.Domain.Tools;
using DMO.IntegrationTests.Host;

namespace DMO.IntegrationTests.JobOn;

/// <summary>
/// Transport proofs of the controlled Peso PDF open route of the Job On surface (this slice): the
/// stored bytes of a related Peso are streamed inline from the configured document workspace
/// through the Documents seam; a Peso of another occurrence is never served; a not-generated file
/// is a typed 409; and the route is gated by the owning <c>job-on-view</c> policy exactly like the
/// consult surface it feeds.
/// </summary>
/// <remarks>
/// The route carries only identities: the deterministic target is resolved and the bytes are read
/// by the Documents application contract — no absolute path and no <c>file:///</c> target crosses
/// this surface, and the denial outcomes are typed (404 <c>peso-not-found</c> for a non-related
/// Peso, 409 <c>pdf-not-generated</c> for a missing file, typed refusal tokens for workspace
/// conditions).
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class JobOnOutputsEndpointsTests
{
    private const string JobOnReference = "REF-OPEN";

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x01, 0x02, 0x03];

    /// <summary>The open route streams the EXACT stored bytes of a related Peso, inline
    /// (<c>application/pdf</c>), and never exposes a path in the response.</summary>
    [Fact]
    public async Task Open_ResturnsTheStoredBytesOfARelatedPesoInline()
    {
        var store = new P2T04TestStore();
        var (jobOnId, pesoId) = SeedOutput(store, pdf: PdfBytes);

        using var factory = P2T04TestHost.ForUser([ViewOnly()], store);
        using var client = factory.CreateClient();

        using var response = await P2T04TestHost.GetAsync(
            client,
            $"/jobon/{jobOnId}/pesos/{pesoId}/peso-pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(PdfBytes, bytes);
    }

    /// <summary>The open route refuses a Peso that is not an output of THIS occurrence: the
    /// related-peso scope is enforced (a Peso of another production is never served).</summary>
    [Fact]
    public async Task Open_RefusesAPesoOfAnotherOccurrenceWithNotFound()
    {
        var store = new P2T04TestStore();
        var (_, pesoId) = SeedOutput(store, pdf: PdfBytes);
        var other = store.SeedJobOn("REF-OTHER", "2000");

        using var factory = P2T04TestHost.ForUser([ViewOnly()], store);
        using var client = factory.CreateClient();

        using var response = await P2T04TestHost.GetAsync(
            client,
            $"/jobon/{other.JobOnId.Value}/pesos/{pesoId}/peso-pdf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var payload = await ReadJsonAsync(response);
        Assert.Equal("peso-not-found", payload.GetProperty("reason").GetString());
        Assert.Equal(other.JobOnId.Value, payload.GetProperty("jobonId").GetGuid());
        Assert.Equal(pesoId, payload.GetProperty("pesoId").GetGuid());

        // The refusal served nothing and wrote nothing: both occurrences stay intact.
        Assert.Equal(2, store.JobOnCount);
    }

    /// <summary>The open route refuses a non-existent occurrence with 404.</summary>
    [Fact]
    public async Task Open_RefusesAMissingOccurrenceWithNotFound()
    {
        var store = new P2T04TestStore();
        var (_, pesoId) = SeedOutput(store, pdf: PdfBytes);
        var missing = Guid.NewGuid();

        using var factory = P2T04TestHost.ForUser([ViewOnly()], store);
        using var client = factory.CreateClient();

        using var response = await P2T04TestHost.GetAsync(
            client,
            $"/jobon/{missing}/pesos/{pesoId}/peso-pdf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var payload = await ReadJsonAsync(response);
        Assert.Equal("not-found", payload.GetProperty("reason").GetString());
        Assert.Equal(missing, payload.GetProperty("jobonId").GetGuid());
    }

    /// <summary>The open route reports a related Peso without a generated PDF as the typed 409
    /// <c>pdf-not-generated</c> — a state, never an error page and never a dead-file response.</summary>
    [Fact]
    public async Task Open_RefusesANotGeneratedPdfWithTheTypedToken()
    {
        var store = new P2T04TestStore();
        var (jobOnId, pesoId) = SeedOutput(store, pdf: null);

        using var factory = P2T04TestHost.ForUser([ViewOnly()], store);
        using var client = factory.CreateClient();

        using var response = await P2T04TestHost.GetAsync(
            client,
            $"/jobon/{jobOnId}/pesos/{pesoId}/peso-pdf");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var payload = await ReadJsonAsync(response);
        Assert.Equal("pdf-not-generated", payload.GetProperty("reason").GetString());
        Assert.Equal(pesoId, payload.GetProperty("pesoId").GetGuid());
    }

    /// <summary>The open route is a consult route: a caller holding ONLY <c>job-on-create</c> is
    /// denied with 403 and no byte is ever served.</summary>
    [Fact]
    public async Task Open_DeniesACallerWithoutJobOnView()
    {
        var store = new P2T04TestStore();
        var (jobOnId, pesoId) = SeedOutput(store, pdf: PdfBytes);

        using var factory = P2T04TestHost.ForUser(
            [P2T04TestHost.Definition(ModuleCatalog.JobOnCreate, "Job On Create", "job-on", "Job On")],
            store);
        using var client = factory.CreateClient();

        using var response = await P2T04TestHost.GetAsync(
            client,
            $"/jobon/{jobOnId}/pesos/{pesoId}/peso-pdf");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("%PDF", body, StringComparison.Ordinal);
    }

    /// <summary>The open route never serves an anonymous caller any protected byte.</summary>
    [Fact]
    public async Task Open_DeniesAnAnonymousCaller()
    {
        var store = new P2T04TestStore();
        var (jobOnId, pesoId) = SeedOutput(store, pdf: PdfBytes);

        using var factory = P2T04TestHost.ForAnonymous(P2T04TestHost.AllGranted(), store);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync($"/jobon/{jobOnId}/pesos/{pesoId}/peso-pdf");

        var status = (int)response.StatusCode;
        Assert.True(
            status is 401 or 403 || status is >= 300 and < 400,
            $"An anonymous caller must be denied on the document route; observed {status}.");

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("%PDF", body, StringComparison.Ordinal);
    }

    /// <summary>Seeds a production with a CM context and (optionally) its Peso PDF.</summary>
    private static (Guid JobOnId, Guid PesoId) SeedOutput(P2T04TestStore store, byte[]? pdf)
    {
        var tool = store.SeedTool(ToolType.Cm, "5447T173", "LOTE-OPEN");
        var context = new ToolContext(
            ToolContextType.Cm,
            Guid.NewGuid(),
            JobOnId.New(),
            tool.ToolId,
            new ToolContextSnapshot(ToolType.Cm, "5447T173", "LOTE-OPEN"));
        var occurrence = store.SeedJobOn(JobOnReference, "1000", contexts: [context]);

        var pesoId = store.SeedPeso(context.ContextId);
        if (pdf is not null)
        {
            store.SeedPesoPdf(pesoId, $"Peso_{JobOnReference}_B1.pdf", pdf);
        }

        return (occurrence.JobOnId.Value, pesoId);
    }

    private static ModuleDefinition ViewOnly() =>
        P2T04TestHost.Definition(ModuleCatalog.JobOnView, "Job On View", "job-on", "Job On");

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}