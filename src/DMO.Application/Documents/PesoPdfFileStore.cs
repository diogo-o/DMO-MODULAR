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
}