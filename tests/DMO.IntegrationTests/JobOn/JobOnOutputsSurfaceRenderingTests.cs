using System.Net;
using System.Text.RegularExpressions;
using DMO.Application.Access;
using DMO.Domain.JobOn;
using DMO.Domain.Tools;
using DMO.IntegrationTests.Host;

namespace DMO.IntegrationTests.JobOn;

/// <summary>
/// Rendered-surface proofs of the Job On Outputs do Controlo section (this slice: the Peso PDF
/// output only): the REAL Razor sheet over the REAL <c>JobOnControlOutputsService</c> and the
/// store-backed seams.
/// </summary>
/// <remarks>
/// The section composes the occurrence (Job On repository), the related Pesos (the Controlo read
/// seam over the real <c>cm_contexts</c> anchors) and the Peso PDF availability (the Documents
/// seam); only an AVAILABLE output renders an open action (a plain link to the controlled
/// application route — never a filesystem path), a not-generated output renders information only
/// with no dead button, and an occurrence without related Pesos renders an honest empty region with
/// no fabricated output. The production facts (Referência/Produção/Máquina/Data) stay in the facts
/// region above and are never repeated by the outputs section.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class JobOnOutputsSurfaceRenderingTests
{
    private const string JobOnReference = "REF-OUTPUTS";
    private const string PdfFileName = $"Peso_{JobOnReference}_B1.pdf";

    /// <summary>JobOn with a related Peso whose PDF exists → the output renders AVAILABLE with the
    /// controlled open route and the file name, never a filesystem path.</summary>
    [Fact]
    public async Task Outputs_WithPesoAndExistingPdfRendersAvailableWithTheControlledOpenAction()
    {
        var store = new P2T04TestStore();
        var (jobOnId, pesoId) = SeedOutput(store, pdf: [0x25, 0x50, 0x44, 0x46, 0x01, 0x02]);

        using var factory = P2T04TestHost.ForUser([ViewOnly()], store);
        using var client = factory.CreateClient();

        using var response = await P2T04TestHost.GetAsync(client, $"/jobon/{jobOnId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-dmo-jobon-outputs=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-dmo-peso-output=\"{pesoId}\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-peso-output-availability=\"available\"", html, StringComparison.Ordinal);

        // The open action is a plain link to the CONTROLLED application route (identifiers only).
        Assert.Contains(
            $"href=\"/jobon/{jobOnId}/pesos/{pesoId}/peso-pdf\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains("data-dmo-peso-output-open=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-dmo-peso-output-file=\"true\">{PdfFileName}", html, StringComparison.Ordinal);

        // No filesystem URI and no absolute path is exposed anywhere on the page.
        Assert.DoesNotContain("file://", html, StringComparison.OrdinalIgnoreCase);
        var openTargets = Regex.Matches(html, "href=\"([^\"]*peso-pdf)\"").Select(match => match.Groups[1].Value).ToList();
        Assert.Equal(new[] { $"/jobon/{jobOnId}/pesos/{pesoId}/peso-pdf" }, openTargets);

        // The not-generated state is NOT rendered for an available output.
        Assert.DoesNotContain("Ainda não gerado", html, StringComparison.Ordinal);
    }

    /// <summary>JobOn with a related Peso whose PDF was not generated yet → the output renders
    /// Ainda não gerado and NO open action (no dead button, no route).</summary>
    [Fact]
    public async Task Outputs_WithPesoWithoutPdfRendersNotGeneratedWithNoAction()
    {
        var store = new P2T04TestStore();
        var (jobOnId, pesoId) = SeedOutput(store, pdf: null);

        using var factory = P2T04TestHost.ForUser([ViewOnly()], store);
        using var client = factory.CreateClient();

        using var response = await P2T04TestHost.GetAsync(client, $"/jobon/{jobOnId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains($"data-dmo-peso-output=\"{pesoId}\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-peso-output-availability=\"not-generated\"", html, StringComparison.Ordinal);
        Assert.Contains("data-dmo-peso-output-not-generated=\"true\">Ainda não gerado", html, StringComparison.Ordinal);

        // No open action exists for a not-generated output: no link, no open hook and no route.
        Assert.DoesNotContain("data-dmo-peso-output-open=\"true\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("peso-pdf", html, StringComparison.Ordinal);
        Assert.DoesNotContain("file://", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>JobOn without any related Peso → the section renders an honest empty region and NO
    /// fabricated Peso output.</summary>
    [Fact]
    public async Task Outputs_WithoutRelatedPesoRendersNoPesoOutputAtAll()
    {
        var store = new P2T04TestStore();
        var tool = store.SeedTool(ToolType.Cm, "5447T173", "LOTE-OUTPUTS");
        var occurrence = store.SeedJobOn(
            JobOnReference,
            "1000",
            contexts:
            [
                new ToolContext(
                    ToolContextType.Cm,
                    Guid.NewGuid(),
                    JobOnId.New(),
                    tool.ToolId,
                    new ToolContextSnapshot(ToolType.Cm, "5447T173", "LOTE-OUTPUTS")),
            ]);

        using var factory = P2T04TestHost.ForUser([ViewOnly()], store);
        using var client = factory.CreateClient();

        using var response = await P2T04TestHost.GetAsync(client, $"/jobon/{occurrence.JobOnId.Value}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-dmo-jobon-outputs=\"true\"", html, StringComparison.Ordinal);
        // The empty-state message comes from the shared partial (HTML-encoded non-ASCII); the
        // assertion uses the ASCII-only fragment.
        Assert.Contains("tem Pesos de controlo associados", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-dmo-peso-output=\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Ainda não gerado", html, StringComparison.Ordinal);
        Assert.DoesNotContain("peso-pdf", html, StringComparison.Ordinal);

        // The empty state of the OUTPUTS section does not fabricate a Peso row either.
        Assert.Contains("data-dmo-state=\"empty\"", html, StringComparison.Ordinal);
    }

    /// <summary>The outputs section is compact: it never repeats the production facts that are
    /// already visible in the facts region of the sheet.</summary>
    [Fact]
    public async Task Outputs_SectionDoesNotRepeatTheProductionFacts()
    {
        var store = new P2T04TestStore();
        var (jobOnId, pesoId) = SeedOutput(store, pdf: [0x25, 0x50, 0x44, 0x46]);

        using var factory = P2T04TestHost.ForUser([ViewOnly()], store);
        using var client = factory.CreateClient();

        using var response = await P2T04TestHost.GetAsync(client, $"/jobon/{jobOnId}");
        var html = await response.Content.ReadAsStringAsync();

        var region = Regex.Match(
            html,
            "<section[^>]*data-dmo-jobon-outputs=\"true\"[^>]*>.*?</section>",
            RegexOptions.Singleline);
        Assert.True(region.Success, "The outputs section must be rendered.");

        foreach (var label in new[] { "Referência", "Número de produção", "Máquina", "Data de produção" })
        {
            Assert.DoesNotContain(label, region.Value, StringComparison.Ordinal);
        }

        // The outputs row carries only the Peso identity, its availability and the open action.
        Assert.Contains($"data-dmo-peso-output=\"{pesoId}\"", region.Value, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(region.Value, "data-dmo-peso-output=\""));
    }

    /// <summary>Seeds a production with a CM context and (optionally) its Peso PDF; the Peso anchors
    /// on the real cm_contexts row of the occurrence.</summary>
    private static (Guid JobOnId, Guid PesoId) SeedOutput(P2T04TestStore store, byte[]? pdf)
    {
        var tool = store.SeedTool(ToolType.Cm, "5447T173", "LOTE-OUTPUTS");
        var context = new ToolContext(
            ToolContextType.Cm,
            Guid.NewGuid(),
            JobOnId.New(),
            tool.ToolId,
            new ToolContextSnapshot(ToolType.Cm, "5447T173", "LOTE-OUTPUTS"));
        var occurrence = store.SeedJobOn(JobOnReference, "1000", contexts: [context]);

        var pesoId = store.SeedPeso(context.ContextId);
        if (pdf is not null)
        {
            store.SeedPesoPdf(pesoId, PdfFileName, pdf);
        }

        return (occurrence.JobOnId.Value, pesoId);
    }

    private static ModuleDefinition ViewOnly() =>
        P2T04TestHost.Definition(ModuleCatalog.JobOnView, "Job On View", "job-on", "Job On");
}