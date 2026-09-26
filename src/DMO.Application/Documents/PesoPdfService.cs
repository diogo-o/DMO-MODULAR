using DMO.Application.Controlo.Pesos;
using DMO.Application.Controlo.Settings;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;

namespace DMO.Application.Documents;

/// <summary>
/// The Peso PDF generation service contract: generates and stores the Peso PDF from the authority
/// of ONE <c>peso_id</c>, using the SHARED P2-T05 read model and the operator-configured base
/// directory from <c>Controlo_Create → Definições</c>.
/// </summary>
/// <remarks>
/// The service is read-only on the Peso record: it never mutates, never bumps the version and
/// never creates any record — the structured Peso remains the only truth and the PDF is a derived
/// output. Generation is gated by the owning workflow (decided records); an existing deterministic
/// target is never rewritten.</remarks>
public interface IPesoPdfService
{
    /// <summary>
    /// Generates and stores the Peso PDF of <paramref name="pesoId"/>, or returns the typed
    /// refusal/availability outcome.
    /// </summary>
    Task<PesoPdfResult> GenerateAsync(Guid pesoId, CancellationToken cancellationToken);
}

/// <summary>
/// The Peso PDF orchestration: configured base directory → shared Peso sheet read → pure document
/// composition → deterministic naming → pure rendering → atomic storage → typed outcome.
/// </summary>
/// <remarks>
/// <para>
/// Authority: P2-T08 slice (documents handoff): the document derives from the SAME <c>peso_id</c> /
/// read model Create and Approve use (<c>IControloCreateService.GetAsync</c> — no parallel read,
/// no parallel calculation path; the per-row results are printed frozen, never recomputed); the
/// base directory is the operator-configured server-host value from <c>pdf_directory_settings</c>;
/// the target follows the closed convention <c>&lt;reference&gt;/&lt;production-number&gt;/
/// Peso_&lt;reference&gt;_&lt;machine&gt;.pdf</c>; the directory chain is created automatically;
/// history/immutability of a decided Peso is preserved: generation never writes to the Peso and an
/// existing target file is never silently regenerated.</para>
/// <para>
/// Preconditions, exactly: the Peso exists; it is DECIDED (aprovado / nao_aprovado — documents
/// become available after the decision); it is production-bound (the Job On traversal facts exist —
/// a pending <c>Job On por associar</c> Peso has no document target). The workspace is probed with
/// the accepted <see cref="IPdfDirectoryProbe"/> BEFORE any directory creation or write.</para>
/// </remarks>
public sealed class PesoPdfService : IPesoPdfService
{
    private readonly IPdfDirectorySettingsRepository _pdfDirectory;
    private readonly IPdfDirectoryProbe _directoryProbe;
    private readonly IControloCreateService _create;
    private readonly IPesoPdfFileStore _files;
    private readonly IPesoPdfRenderer _renderer;

    /// <summary>Creates the service over the settings repositories, the shared read, the file store
    /// and the renderer.</summary>
    public PesoPdfService(
        IPdfDirectorySettingsRepository pdfDirectory,
        IPdfDirectoryProbe directoryProbe,
        IControloCreateService create,
        IPesoPdfFileStore files,
        IPesoPdfRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(pdfDirectory);
        ArgumentNullException.ThrowIfNull(directoryProbe);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(renderer);
        _pdfDirectory = pdfDirectory;
        _directoryProbe = directoryProbe;
        _create = create;
        _files = files;
        _renderer = renderer;
    }

    /// <inheritdoc />
    public async Task<PesoPdfResult> GenerateAsync(
        Guid pesoId,
        CancellationToken cancellationToken)
    {
        // 1. The configured base directory (P2-T05 §12, delta §7): an absent row is the explicit
        //    not-configured state — never an error and never a default path.
        var settings = await _pdfDirectory.GetAsync(cancellationToken);
        if (settings is null)
        {
            return Refuse(
                PesoPdfRefusalReason.PdfDirectoryNotConfigured,
                "Não existe diretório base configurado para documentos; configure o diretório " +
                "em Controlo_Create → Definições.");
        }

        // 2. The workspace is probed BEFORE any creation/write (Q-PDF server-host semantics); a
        //    failed workspace is distinguishable from a missing file.
        if (_directoryProbe.Probe(settings.BaseDirectory) != PdfDirectoryCheckState.Ok)
        {
            return Refuse(
                PesoPdfRefusalReason.WorkspaceUnavailable,
                "O diretório base configurado não está acessível ao servidor (inexistente, não é " +
                "diretório, ou sem permissão de leitura/escrita); verifique Definições.");
        }

        // 3. The SHARED read model — the same peso_id read used by Create and Approve; the
        //    document facts and the target facts (reference/production/machine) come ONLY from it.
        var read = await _create.GetAsync(pesoId, cancellationToken);
        if (read is PesoResult.NotFound)
        {
            return new PesoPdfResult.NotFound(pesoId);
        }

        if (read is not PesoResult.Found(var sheet))
        {
            return new PesoPdfResult.ValidationFailed(["PESO_READ_FAILED"]);
        }

        // 4. Decided precondition: documents become available after the decision
        //    (Create's R8 seam: "A disponibilidade de documentos é apresentada após a aprovação").
        var status = PesoStatusTokens.Parse(sheet.Status);
        if (status is not (PesoStatus.Aprovado or PesoStatus.NaoAprovado))
        {
            return Refuse(
                PesoPdfRefusalReason.NotDecided,
                "Este Peso ainda não foi decidido; o PDF é gerado após a decisão.");
        }

        // 5. Production binding: the document target is derived from the Job On traversal facts;
        //    a pending Peso (Job On por associar) has no target — not an error, a state.
        if (sheet.Production is not { } production)
        {
            return Refuse(
                PesoPdfRefusalReason.ProductionBindingMissing,
                "Este Peso não está associado a uma produção (Job On por associar); não existe " +
                "destino de documento para gerar.");
        }

        // 6. Deterministic naming (closed convention); fails closed on unsafe values.
        if (!PesoPdfNaming.TryCompose(
                production.Reference,
                production.ProductionNumber,
                production.Machine,
                out var target)
            || target is null)
        {
            return Refuse(
                PesoPdfRefusalReason.InvalidFileName,
                "A referência, o número de produção ou a máquina não permitem formar um destino " +
                "de documento seguro; nada foi gerado.");
        }

        // 7. Pure composition + rendering: the PDF shows the frozen facts of the shared read
        //    model with the fixed identification reading order and the per-CM comparison.
        var document = PesoPdfComposer.Compose(sheet);
        var bytes = _renderer.Render(document, DateTimeOffset.UtcNow);

        // 8. Atomic storage with automatic directory creation; an existing target is never
        //    overwritten (frozen outputs are not silently regenerated).
        var write = await _files.WriteAsync(
            settings.BaseDirectory,
            target.RelativeDirectory,
            target.FileName,
            bytes,
            cancellationToken);

        var output = new PesoPdfOutput(
            sheet.PesoId,
            sheet.Version,
            target.FileName,
            target.RelativePath,
            write.Bytes);

        return write.State switch
        {
            PesoPdfFileWriteState.Written => new PesoPdfResult.Generated(output),
            PesoPdfFileWriteState.AlreadyExists => new PesoPdfResult.AlreadyAvailable(output),
            PesoPdfFileWriteState.WorkspaceUnavailable => Refuse(
                PesoPdfRefusalReason.WorkspaceUnavailable,
                "O diretório base configurado não está acessível ao servidor ao gravar o " +
                "documento; verifique Definições e repita."),
            _ => Refuse(
                PesoPdfRefusalReason.WriteFailed,
                "Não foi possível gravar o ficheiro PDF no diretório configurado; repita ou " +
                "verifique o diretório em Definições."),
        };
    }

    private static PesoPdfResult Refuse(PesoPdfRefusalReason reason, string message) =>
        new PesoPdfResult.Refused(reason, message);
}