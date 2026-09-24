namespace DMO.Application.Documents;

/// <summary>
/// The typed outcome of one Peso PDF storage write.
/// </summary>
public enum PesoPdfFileWriteState
{
    /// <summary>The file was written at the deterministic target (directories created as needed).</summary>
    Written,

    /// <summary>The deterministic target already holds the file; nothing was overwritten.</summary>
    AlreadyExists,

    /// <summary>The base workspace is not usable from the server process (missing base, base is
    /// not a directory, or directory creation failed) — distinguishable from a missing file.</summary>
    WorkspaceUnavailable,

    /// <summary>The write failed for another infrastructure reason.</summary>
    WriteFailed,
}

/// <summary>The typed file-store outcome: the state plus the byte count of a written file.</summary>
public sealed record PesoPdfFileWriteResult(PesoPdfFileWriteState State, long Bytes);

/// <summary>
/// The Peso PDF storage adapter contract: writes one PDF under the configured base directory at
/// the deterministic relative target, creating the <c>&lt;reference&gt;/&lt;production-number&gt;/</c>
/// directories when missing — the operator is never asked to create directories manually.
/// </summary>
/// <remarks>
/// The target never exists partially: the file is written to a unique temporary name inside the
/// target directory and atomically moved to the final name; when the final name already exists the
/// write is refused (<see cref="PesoPdfFileWriteState.AlreadyExists"/>) — a frozen output is never
/// silently overwritten. The adapter knows only the base directory and the relative target: it
/// never interprets the segments and never redefines reference/production as authority.</remarks>
public interface IPesoPdfFileStore
{
    /// <summary>
    /// Stores the PDF bytes at <c>baseDirectory/relativeDirectory/fileName</c>, creating the
    /// directory chain when needed, and returns the typed outcome.
    /// </summary>
    Task<PesoPdfFileWriteResult> WriteAsync(
        string baseDirectory,
        string relativeDirectory,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the bytes of an EXISTING stored file (the attachment read of the email slice):
    /// <c>Missing</c> when the deterministic target holds no file (never regenerated for
    /// sending), <c>Failed</c> on any other IO failure — never conflated with missing.
    /// </summary>
    Task<PesoPdfFileReadResult> ReadAsync(
        string baseDirectory,
        string relativeDirectory,
        string fileName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reports whether the deterministic target holds the file WITHOUT reading its bytes (the
    /// availability read of the outputs slice): <c>Missing</c> when the deterministic target holds
    /// no file, <c>Failed</c> on any other IO failure — never conflated with missing.
    /// </summary>
    Task<PesoPdfFilePresenceResult> ExistsAsync(
        string baseDirectory,
        string relativeDirectory,
        string fileName,
        CancellationToken cancellationToken);
}

/// <summary>The typed outcome of one byte-free presence check of a stored document.</summary>
public sealed record PesoPdfFilePresenceResult(PesoPdfFilePresenceState State)
{
    /// <summary>The deterministic target holds the file.</summary>
    public static PesoPdfFilePresenceResult Found() => new(PesoPdfFilePresenceState.Found);

    /// <summary>The deterministic target holds no file.</summary>
    public static PesoPdfFilePresenceResult Missing() => new(PesoPdfFilePresenceState.Missing);

    /// <summary>Another infrastructure failure (permissions, IO) — not a missing file.</summary>
    public static PesoPdfFilePresenceResult Failed() => new(PesoPdfFilePresenceState.Failed);
}

/// <summary>The typed presence states of the byte-free document check.</summary>
public enum PesoPdfFilePresenceState
{
    /// <summary>The file exists at the deterministic target.</summary>
    Found,

    /// <summary>The deterministic target holds no file.</summary>
    Missing,

    /// <summary>Another IO failure — distinguishable from a missing file.</summary>
    Failed,
}

/// <summary>The typed outcome of one Peso PDF attachment read.</summary>
public sealed record PesoPdfFileReadResult(PesoPdfFileReadState State, byte[]? Bytes)
{
    /// <summary>Creates the found-file outcome.</summary>
    public static PesoPdfFileReadResult Found(byte[] bytes) => new(PesoPdfFileReadState.Found, bytes);

    /// <summary>The deterministic target holds no file.</summary>
    public static PesoPdfFileReadResult Missing() => new(PesoPdfFileReadState.Missing, null);

    /// <summary>Another infrastructure failure (permissions, IO) — not a missing file.</summary>
    public static PesoPdfFileReadResult Failed() => new(PesoPdfFileReadState.Failed, null);
}

/// <summary>The typed read states of the attachment read.</summary>
public enum PesoPdfFileReadState
{
    /// <summary>The file exists and its bytes were read.</summary>
    Found,

    /// <summary>The deterministic target holds no file.</summary>
    Missing,

    /// <summary>Another IO failure — distinguishable from a missing file.</summary>
    Failed,
}

/// <summary>
/// The <see cref="IPesoPdfFileStore"/> over the real server-host filesystem (Q-PDF semantics: the
/// base directory is server-host configuration; storage executes under the server process account).
/// </summary>
/// <remarks>
/// A non-absolute or missing base is <c>workspace-unavailable</c> (the service probes the
/// configured base before calling, so this is the defensive boundary); an existing non-directory
/// base is also <c>workspace-unavailable</c>; directory creation failures and write failures are
/// typed and never conflated with <c>already-exists</c> or with a missing file.</remarks>
public sealed class ServerHostPesoPdfFileStore : IPesoPdfFileStore
{
    /// <inheritdoc />
    public Task<PesoPdfFileWriteResult> WriteAsync(
        string baseDirectory,
        string relativeDirectory,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            if (!Path.IsPathRooted(baseDirectory))
            {
                return Task.FromResult(new PesoPdfFileWriteResult(
                    PesoPdfFileWriteState.WorkspaceUnavailable,
                    Bytes: 0));
            }

            if (File.Exists(baseDirectory) || !Directory.Exists(baseDirectory))
            {
                return Task.FromResult(new PesoPdfFileWriteResult(
                    PesoPdfFileWriteState.WorkspaceUnavailable,
                    Bytes: 0));
            }

            // Create <base>/<reference>/<production-number>/ automatically; the operator is never
            // asked to create directories manually and no production_id is minted here.
            var targetDirectory = Path.Combine(baseDirectory, relativeDirectory);
            Directory.CreateDirectory(targetDirectory);

            var target = Path.Combine(targetDirectory, fileName);
            if (File.Exists(target))
            {
                return Task.FromResult(new PesoPdfFileWriteResult(
                    PesoPdfFileWriteState.AlreadyExists,
                    Bytes: 0));
            }

            // Atomic install: write to a unique temp name in the SAME directory, then move with
            // overwrite disabled — the final name can never appear half-written and a raced
            // concurrent write surfaces as AlreadyExists instead of an overwrite.
            var temporary = Path.Combine(
                targetDirectory,
                $".{fileName}.tmp-{Guid.NewGuid():N}");

            try
            {
                File.WriteAllBytes(temporary, content);
                File.Move(temporary, target, overwrite: false);
            }
            catch (IOException) when (File.Exists(target))
            {
                return Task.FromResult(new PesoPdfFileWriteResult(
                    PesoPdfFileWriteState.AlreadyExists,
                    Bytes: 0));
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            return Task.FromResult(new PesoPdfFileWriteResult(
                PesoPdfFileWriteState.Written,
                content.LongLength));
        }
        catch (UnauthorizedAccessException)
        {
            // Permission failures belong to the workspace condition, never to the file itself.
            return Task.FromResult(new PesoPdfFileWriteResult(
                PesoPdfFileWriteState.WorkspaceUnavailable,
                Bytes: 0));
        }
        catch (IOException)
        {
            return Task.FromResult(new PesoPdfFileWriteResult(
                PesoPdfFileWriteState.WriteFailed,
                Bytes: 0));
        }
        catch (ArgumentException)
        {
            return Task.FromResult(new PesoPdfFileWriteResult(
                PesoPdfFileWriteState.WorkspaceUnavailable,
                Bytes: 0));
        }
    }

    /// <inheritdoc />
    public Task<PesoPdfFileReadResult> ReadAsync(
        string baseDirectory,
        string relativeDirectory,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        try
        {
            if (!Path.IsPathRooted(baseDirectory)
                || File.Exists(baseDirectory)
                || !Directory.Exists(baseDirectory))
            {
                return Task.FromResult(PesoPdfFileReadResult.Missing());
            }

            var target = Path.Combine(baseDirectory, relativeDirectory, fileName);

            if (!File.Exists(target))
            {
                return Task.FromResult(PesoPdfFileReadResult.Missing());
            }

            return Task.FromResult(PesoPdfFileReadResult.Found(File.ReadAllBytes(target)));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(PesoPdfFileReadResult.Failed());
        }
        catch (IOException)
        {
            return Task.FromResult(PesoPdfFileReadResult.Failed());
        }
        catch (ArgumentException)
        {
            return Task.FromResult(PesoPdfFileReadResult.Failed());
        }
    }

    /// <inheritdoc />
    public Task<PesoPdfFilePresenceResult> ExistsAsync(
        string baseDirectory,
        string relativeDirectory,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        try
        {
            var target = Path.Combine(baseDirectory, relativeDirectory, fileName);

            return Task.FromResult(File.Exists(target)
                ? PesoPdfFilePresenceResult.Found()
                : PesoPdfFilePresenceResult.Missing());
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(PesoPdfFilePresenceResult.Failed());
        }
        catch (ArgumentException)
        {
            return Task.FromResult(PesoPdfFilePresenceResult.Failed());
        }
        catch (IOException)
        {
            return Task.FromResult(PesoPdfFilePresenceResult.Failed());
        }
    }
}