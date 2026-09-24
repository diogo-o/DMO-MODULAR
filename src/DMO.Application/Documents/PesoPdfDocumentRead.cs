using DMO.Application.ControloCreate;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;

namespace DMO.Application.Documents;

/// <summary>
/// The Peso PDF document read: resolves the deterministic target of ONE <c>peso_id</c> from the
/// SHARED P2-T05 read model and the operator-configured base directory, and reports the file
/// availability — or reads the stored bytes — always through the same <c>Documents</c> storage
/// adapter.
/// </summary>
/// <remarks>
/// <para>
/// The read is the read-only counterpart of <see cref="IPesoPdfService"/>. It derives the target
/// from exactly the same facts and the same setting (no second source): the configured base
/// directory of <c>Controlo_Create → Definições</c> and the Job On traversal facts of the SHARED
/// P2-T05 read model via the closed naming convention. It never writes, never generates, never
/// mints anything and never exposes a path: the consuming surface receives a typed state or the
/// bytes, and the caller never owns a filesystem location — <c>file:///</c> and absolute paths
/// never cross the application boundary.</para>
/// <para>
/// Availability is about the FILE, not the workflow: a decided Peso whose PDF exists is
/// <c>Available</c> and an undecided Peso whose PDF has never been generated is
/// <c>NotGenerated</c> — the decision gate belongs to generation, never to this read. A missing
/// configured workspace is a typed refusal, never conflated with a missing file (P2-T08
/// availability rule).</para>
/// </remarks>
public interface IPesoPdfDocumentRead
{
    /// <summary>
    /// Resolves the deterministic document target of <paramref name="pesoId"/> and reports its
    /// availability state (no bytes are read).
    /// </summary>
    Task<PesoPdfAvailabilityResult> GetAvailabilityAsync(
        Guid pesoId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the stored bytes of the Peso PDF of <paramref name="pesoId"/> from the configured
    /// base directory, or returns the typed non-available outcome.
    /// </summary>
    Task<PesoPdfContentResult> ReadAsync(
        Guid pesoId,
        CancellationToken cancellationToken);
}

/// <summary>The typed availability of one Peso PDF document target.</summary>
/// <remarks>
/// <see cref="Available"/> carries the deterministic file name and the relative convention path;
/// <see cref="NotGenerated"/> is a missing file, never an error; <see cref="NotFound"/> is a Peso
/// that does not exist; <see cref="Refused"/> is every configured-workspace/infrastructure
/// condition, never conflated with a missing file.
/// </remarks>
public abstract record PesoPdfAvailabilityResult
{
    private PesoPdfAvailabilityResult()
    {
    }

    /// <summary>The deterministic target holds the file.</summary>
    public sealed record Available(string FileName, string RelativePath) : PesoPdfAvailabilityResult;

    /// <summary>The deterministic target holds no file yet (the Peso may or may not be decided).</summary>
    public sealed record NotGenerated : PesoPdfAvailabilityResult;

    /// <summary>The Peso does not exist.</summary>
    public sealed record NotFound(Guid PesoId) : PesoPdfAvailabilityResult;

    /// <summary>A typed refusal (configuration/workspace/infrastructure); never conflated with a missing file.</summary>
    public sealed record Refused(PesoPdfDocumentReadRefusalReason Reason, string Message) : PesoPdfAvailabilityResult;
}

/// <summary>The typed outcome of one Peso PDF content read.</summary>
public abstract record PesoPdfContentResult
{
    private PesoPdfContentResult()
    {
    }

    /// <summary>The stored bytes of the existing file at the deterministic target.</summary>
    public sealed record Found(string FileName, string RelativePath, byte[] Content) : PesoPdfContentResult;

    /// <summary>The deterministic target holds no file yet.</summary>
    public sealed record NotGenerated : PesoPdfContentResult;

    /// <summary>The Peso does not exist.</summary>
    public sealed record NotFound(Guid PesoId) : PesoPdfContentResult;

    /// <summary>A typed refusal (configuration/workspace/infrastructure); never conflated with a missing file.</summary>
    public sealed record Refused(PesoPdfDocumentReadRefusalReason Reason, string Message) : PesoPdfContentResult;
}

/// <summary>The typed refusal vocabulary of the Peso PDF document read.</summary>
public enum PesoPdfDocumentReadRefusalReason
{
    /// <summary>No base directory is configured in Controlo_Create → Definições.</summary>
    PdfDirectoryNotConfigured,

    /// <summary>The configured workspace is not usable from the server process — distinguishable
    /// from a missing file.</summary>
    WorkspaceUnavailable,

    /// <summary>The Peso has no production binding (Job On por associar) — no document target exists.</summary>
    ProductionBindingMissing,

    /// <summary>A reference/production/machine value cannot form a safe document target.</summary>
    InvalidFileName,

    /// <summary>The stored file could not be read for another infrastructure reason — never
    /// conflated with a missing file.</summary>
    ReadFailed,
}

/// <summary>
/// The Peso PDF document read orchestration: configured base directory → shared Peso sheet read →
/// closed deterministic naming → typed availability/content through the file store.
/// </summary>
/// <remarks>
/// <para>
/// Every branch reuses the SAME resolution of <see cref="IPesoPdfService"/> (settings, workspace
/// probe, shared read, naming); only the final act differs — generation writes, this read reports
/// or reads. No parallel read model and no recomputation exists: the traversal facts come from the
/// same <c>IControloCreateService.GetAsync</c> the documents and the Controlo surfaces render.</para>
/// <para>
/// Read-only: no write, no version bump, no creation; an existing file is never touched and a
/// missing file is never generated for reading (a document is only ever read after the owning
/// workflow generated it).</para>
/// </remarks>
public sealed class PesoPdfDocumentReadService : IPesoPdfDocumentRead
{
    private readonly IPdfDirectorySettingsRepository _pdfDirectory;
    private readonly IPdfDirectoryProbe _directoryProbe;
    private readonly IControloCreateService _create;
    private readonly IPesoPdfFileStore _files;

    /// <summary>Creates the read over the settings repository, the workspace probe, the shared
    /// Peso read and the file store.</summary>
    public PesoPdfDocumentReadService(
        IPdfDirectorySettingsRepository pdfDirectory,
        IPdfDirectoryProbe directoryProbe,
        IControloCreateService create,
        IPesoPdfFileStore files)
    {
        ArgumentNullException.ThrowIfNull(pdfDirectory);
        ArgumentNullException.ThrowIfNull(directoryProbe);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(files);
        _pdfDirectory = pdfDirectory;
        _directoryProbe = directoryProbe;
        _create = create;
        _files = files;
    }

    /// <inheritdoc />
    public async Task<PesoPdfAvailabilityResult> GetAvailabilityAsync(
        Guid pesoId,
        CancellationToken cancellationToken) =>
        await ResolveAsync(pesoId, readContent: false, cancellationToken) switch
        {
            Resolution.Available(var fileName, var relativePath, _) =>
                new PesoPdfAvailabilityResult.Available(fileName, relativePath),
            Resolution.NotGenerated => new PesoPdfAvailabilityResult.NotGenerated(),
            Resolution.NotFound(var id) => new PesoPdfAvailabilityResult.NotFound(id),
            Resolution.Refused(var reason, var message) =>
                new PesoPdfAvailabilityResult.Refused(reason, message),
            _ => throw new InvalidOperationException("Unknown document availability resolution."),
        };

    /// <inheritdoc />
    public async Task<PesoPdfContentResult> ReadAsync(
        Guid pesoId,
        CancellationToken cancellationToken) =>
        await ResolveAsync(pesoId, readContent: true, cancellationToken) switch
        {
            Resolution.Available(var fileName, var relativePath, var content) =>
                content is null
                    ? new PesoPdfContentResult.Refused(
                        PesoPdfDocumentReadRefusalReason.ReadFailed,
                        "O ficheiro do documento não pôde ser lido; tente novamente ou verifique Definições.")
                    : new PesoPdfContentResult.Found(fileName, relativePath, content),
            Resolution.NotGenerated => new PesoPdfContentResult.NotGenerated(),
            Resolution.NotFound(var id) => new PesoPdfContentResult.NotFound(id),
            Resolution.Refused(var reason, var message) =>
                new PesoPdfContentResult.Refused(reason, message),
            _ => throw new InvalidOperationException("Unknown document content resolution."),
        };

    /// <summary>
    /// The single resolution of the deterministic target of ONE Peso: the configured base directory
    /// (an absent row is the explicit not-configured state — never an error and never a default
    /// path), the workspace probe, the SHARED Peso read, the production traversal facts and the
    /// closed naming; then the typed file read through the SAME store adapter (a missing file is
    /// never conflated with an inaccessible workspace). <paramref name="readContent"/> keeps the
    /// byte read out of the availability-only path.
    /// </summary>
    private async Task<Resolution> ResolveAsync(
        Guid pesoId,
        bool readContent,
        CancellationToken cancellationToken)
    {
        var settings = await _pdfDirectory.GetAsync(cancellationToken);
        if (settings is null)
        {
            return Refuse(
                PesoPdfDocumentReadRefusalReason.PdfDirectoryNotConfigured,
                "Não existe diretório base configurado para documentos; configure o diretório " +
                "em Controlo_Create → Definições.");
        }

        if (_directoryProbe.Probe(settings.BaseDirectory) != PdfDirectoryCheckState.Ok)
        {
            return Refuse(
                PesoPdfDocumentReadRefusalReason.WorkspaceUnavailable,
                "O diretório base configurado não está acessível ao servidor; verifique Definições.");
        }

        var read = await _create.GetAsync(pesoId, cancellationToken);
        if (read is PesoResult.NotFound)
        {
            return new Resolution.NotFound(pesoId);
        }

        if (read is not PesoResult.Found(var sheet))
        {
            return Refuse(
                PesoPdfDocumentReadRefusalReason.ReadFailed,
                "Não foi possível ler o Peso para resolver o destino do documento.");
        }

        // The document target comes ONLY from the Job On traversal facts of the shared read model;
        // a pending Peso (Job On por associar) has no target — a state, not an error.
        if (sheet.Production is not { } production)
        {
            return Refuse(
                PesoPdfDocumentReadRefusalReason.ProductionBindingMissing,
                "Este Peso não está associado a uma produção; não existe destino de documento.");
        }

        if (!PesoPdfNaming.TryCompose(
                production.Reference,
                production.ProductionNumber,
                production.Machine,
                out var target)
            || target is null)
        {
            return Refuse(
                PesoPdfDocumentReadRefusalReason.InvalidFileName,
                "A referência, o número de produção ou a máquina não permitem formar um destino " +
                "de documento seguro.");
        }

        // The file existence/read stays inside the Documents storage adapter; a missing file is
        // never conflated with an inaccessible workspace. The availability path never reads bytes.
        if (readContent)
        {
            var file = await _files.ReadAsync(
                settings.BaseDirectory,
                target.RelativeDirectory,
                target.FileName,
                cancellationToken);

            return file.State switch
            {
                PesoPdfFileReadState.Found => new Resolution.Available(
                    target.FileName,
                    target.RelativePath,
                    file.Bytes),
                PesoPdfFileReadState.Missing => new Resolution.NotGenerated(),
                PesoPdfFileReadState.Failed => Refuse(
                    PesoPdfDocumentReadRefusalReason.ReadFailed,
                    "O ficheiro do documento não pôde ser lido do diretório configurado; " +
                    "verifique Definições e tente novamente."),
                _ => throw new InvalidOperationException("Unknown file read state."),
            };
        }

        var presence = await _files.ExistsAsync(
            settings.BaseDirectory,
            target.RelativeDirectory,
            target.FileName,
            cancellationToken);

        return presence.State switch
        {
            PesoPdfFilePresenceState.Found => new Resolution.Available(
                target.FileName,
                target.RelativePath,
                Content: null),
            PesoPdfFilePresenceState.Missing => new Resolution.NotGenerated(),
            PesoPdfFilePresenceState.Failed => Refuse(
                PesoPdfDocumentReadRefusalReason.ReadFailed,
                "O ficheiro do documento não pôde ser verificado no diretório configurado; " +
                "verifique Definições e tente novamente."),
            _ => throw new InvalidOperationException("Unknown file presence state."),
        };
    }

    private static Resolution Refuse(PesoPdfDocumentReadRefusalReason reason, string message) =>
        new Resolution.Refused(reason, message);

    /// <summary>The internal resolution carrier of one document target.</summary>
    private abstract record Resolution
    {
        /// <summary>The deterministic target holds the file; <see cref="Content"/> is present only
        /// when the read path requested the bytes.</summary>
        public sealed record Available(string FileName, string RelativePath, byte[]? Content) : Resolution;

        /// <summary>The deterministic target holds no file yet.</summary>
        public sealed record NotGenerated : Resolution;

        /// <summary>The Peso does not exist.</summary>
        public sealed record NotFound(Guid PesoId) : Resolution;

        /// <summary>A typed refusal; never conflated with a missing file.</summary>
        public sealed record Refused(PesoPdfDocumentReadRefusalReason Reason, string Message) : Resolution;
    }
}