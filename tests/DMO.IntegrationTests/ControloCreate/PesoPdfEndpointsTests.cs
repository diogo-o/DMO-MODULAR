using System.Net;
using System.Text.Json;
using DMO.Application.Access;
using DMO.Domain.Controlo;
using DMO.Domain.Tools;
using DMO.IntegrationTests.ControloApprove;
using PesoId = DMO.Domain.Controlo.PesoId;

namespace DMO.IntegrationTests.ControloCreate;

/// <summary>
/// HTTP-class proofs of the P2-T08 Peso PDF documents slice over the real service stack (real
/// renderer, real server-host file store, real orchestration): generation stores the PDF under the
/// configured base directory at the deterministic convention target (directories created
/// automatically), a second generation answers <c>already-available</c> without overwriting, an
/// undecided Peso is refused <c>not-decided</c>, an absent directory setting is refused
/// <c>pdf-directory-not-configured</c>, and the route is gated by the Controlo CREATE module
/// policy — Create owns the operational document work after the decision, so a create-only caller
/// reaches the route while an approve-only caller is denied (Approve stays focused on the
/// decision).
/// </summary>
/// <remarks>
/// The Peso facts come from the shared in-memory composition (the same store the real
/// ControloCreateService writes through; the P2-T06 review store is reused only to ARRANGE the
/// decided state of the fixture — the documents service itself stays policy-free and reads only
/// the shared Peso sheet); the PDF is stored on the REAL server-host filesystem under a temp base
/// directory, exactly like the accepted Q-PDF server-host semantics.</remarks>
public sealed class PesoPdfEndpointsTests
{
    // ----------------------------------------------------------------------------------------
    // Fixture helpers
    // ----------------------------------------------------------------------------------------

    private static async Task<Guid> SeedApprovedPesoAsync(
        P2T06TestComposition composition,
        string referenceToken)
    {
        var pesoId = await SeedSubmittedPesoAsync(composition, referenceToken);

        var peso = (await composition.Pesos.GetByIdAsync(pesoId, CancellationToken.None))!;
        await composition.Review.DecisionAsync(
            new PesoReviewDecision(
                PesoReviewDecisionId.New(),
                PesoId.From(pesoId),
                PesoReviewDecisionKind.Aprovado,
                P2T06TestHost.ActorUserId,
                DateTimeOffset.UtcNow.AddMinutes(-5),
                Reason: null,
                PesoStatus.Pendente,
                peso.Version,
                DateTimeOffset.UtcNow.AddMinutes(-5)),
            expectedPesoVersion: peso.Version,
            CancellationToken.None);

        return pesoId;
    }

    private static async Task<Guid> SeedSubmittedPesoAsync(
        P2T06TestComposition composition,
        string referenceToken)
    {
        var tool = composition.SeedTool(ToolType.Cm, $"ref-{referenceToken}", "01", Processo.Nnpb, "B1");
        var jobOn = composition.SeedJobOnWithCmContext(
            $"ref-{referenceToken}",
            $"pn-{referenceToken}",
            "B1",
            tool.ToolId.Value,
            ToolType.Cm,
            tool.Reference,
            tool.Lot);

        var cm = jobOn.Contexts.First(context => context.ContextType == ToolContextType.Cm);

        return await composition.SeedReviewablePesoAsync(cmId: cm.ContextId);
    }

    private static async Task ConfigureBaseDirectoryAsync(
        P2T06TestComposition composition,
        string baseDirectory)
    {
        await composition.Pesos.SetAsync(
            new PdfDirectorySettings(
                Guid.NewGuid(),
                baseDirectory,
                Version: 1,
                DateTimeOffset.UtcNow),
            CancellationToken.None);
    }

    // ----------------------------------------------------------------------------------------
    // Generation + storage
    // ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Generate_StoresThePdfAtTheDeterministicTargetAndAnswersAlreadyAvailableOnRepeat()
    {
        using var directory = new TempDirectory();
        var composition = new P2T06TestComposition();
        var pesoId = await SeedApprovedPesoAsync(composition, "PDF1");
        await ConfigureBaseDirectoryAsync(composition, directory.FullPath);

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.CreateOnly(), composition);
        using var client = factory.CreateClient();

        var target = Path.Combine(
            directory.FullPath, "ref-PDF1", "pn-PDF1", "Peso_ref-PDF1_B1.pdf");
        Assert.False(File.Exists(target), "The document must not exist before generation.");

        using (var first = await PostPesoPdfAsync(client, pesoId))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var payload = await ReadJsonAsync(first);

            Assert.Equal("generated", payload.GetProperty("status").GetString());
            Assert.Equal(pesoId, payload.GetProperty("pesoId").GetGuid());
            Assert.Equal("Peso_ref-PDF1_B1.pdf", payload.GetProperty("fileName").GetString());
            Assert.Equal(
                "ref-PDF1/pn-PDF1/Peso_ref-PDF1_B1.pdf",
                payload.GetProperty("relativePath").GetString());
            Assert.True(payload.GetProperty("bytes").GetInt64() > 0);
        }

        // The directory chain was created automatically (never asked of the operator) and the
        // stored file is a real PDF container.
        Assert.True(File.Exists(target));
        var stored = File.ReadAllBytes(target);
        Assert.True(stored.AsSpan().StartsWith("%PDF-1.4"u8));

        // A repeated generation never overwrites: it answers already-available with the SAME file.
        using (var second = await PostPesoPdfAsync(client, pesoId))
        {
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var payload = await ReadJsonAsync(second);
            Assert.Equal("already-available", payload.GetProperty("status").GetString());
            Assert.Equal("Peso_ref-PDF1_B1.pdf", payload.GetProperty("fileName").GetString());
        }

        Assert.Equal(stored, File.ReadAllBytes(target));

        // No temporary debris remains anywhere in the document tree.
        Assert.DoesNotContain(
            Directory.EnumerateFiles(directory.FullPath, "*", SearchOption.AllDirectories),
            path => !path.EndsWith("Peso_ref-PDF1_B1.pdf", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generate_OnAnUndecidedPesoIsRefusedNotDecided()
    {
        using var directory = new TempDirectory();
        var composition = new P2T06TestComposition();
        var pesoId = await SeedSubmittedPesoAsync(composition, "PDF2");
        await ConfigureBaseDirectoryAsync(composition, directory.FullPath);

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.CreateOnly(), composition);
        using var client = factory.CreateClient();

        using var response = await PostPesoPdfAsync(client, pesoId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var payload = await ReadJsonAsync(response);
        Assert.Equal("not-decided", payload.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Generate_WithoutAConfiguredDirectoryIsRefusedNotConfigured()
    {
        var composition = new P2T06TestComposition();
        var pesoId = await SeedApprovedPesoAsync(composition, "PDF3");

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.CreateOnly(), composition);
        using var client = factory.CreateClient();

        using var response = await PostPesoPdfAsync(client, pesoId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var payload = await ReadJsonAsync(response);
        Assert.Equal("pdf-directory-not-configured", payload.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Generate_WithAnInaccessibleWorkspaceIsRefusedWorkspaceUnavailable()
    {
        var composition = new P2T06TestComposition();
        var pesoId = await SeedApprovedPesoAsync(composition, "PDF4");
        await ConfigureBaseDirectoryAsync(
            composition,
            Path.Combine(Path.GetTempPath(), $"dmo-peso-pdf-absent-{Guid.NewGuid():N}"));

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.CreateOnly(), composition);
        using var client = factory.CreateClient();

        using var response = await PostPesoPdfAsync(client, pesoId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var payload = await ReadJsonAsync(response);
        Assert.Equal("workspace-unavailable", payload.GetProperty("reason").GetString());
    }

    // ----------------------------------------------------------------------------------------
    // Access (the owning workflow gate: Create owns the operational document work)
    // ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Generate_IsDeniedToAnApproveOnlyCaller()
    {
        using var directory = new TempDirectory();
        var composition = new P2T06TestComposition();
        var pesoId = await SeedApprovedPesoAsync(composition, "PDF5");
        await ConfigureBaseDirectoryAsync(composition, directory.FullPath);

        // Approve-only grant: the decision surface owns no document execution.
        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await PostPesoPdfAsync(client, pesoId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Generate_IsReachedByACreateCaller()
    {
        using var directory = new TempDirectory();
        var composition = new P2T06TestComposition();
        var pesoId = await SeedApprovedPesoAsync(composition, "PDF6");
        await ConfigureBaseDirectoryAsync(composition, directory.FullPath);

        using var factory = P2T06TestHost.ForUser(P2T06TestHost.CreateOnly(), composition);
        using var client = factory.CreateClient();

        using var response = await PostPesoPdfAsync(client, pesoId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("generated", (await ReadJsonAsync(response)).GetProperty("status").GetString());
    }

    // ----------------------------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------------------------

    private static Task<HttpResponseMessage> PostPesoPdfAsync(HttpClient client, Guid pesoId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/controlo/create/pesos/{pesoId}/peso-pdf");

        return P2T06TestHost.SendAuthenticatedAsync(client, request);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>A real temp directory that removes itself on dispose (best-effort).</summary>
    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            FullPath = Path.Combine(
                Path.GetTempPath(),
                $"dmo-peso-pdf-http-{Guid.NewGuid():N}");
            Directory.CreateDirectory(FullPath);
        }

        public string FullPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(FullPath))
                {
                    Directory.Delete(FullPath, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup only.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup only.
            }
        }
    }
}