namespace DMO.Application.Controlo.Settings;

/// <summary>
/// The server-side PDF-directory accessibility probe (Q-PDF ruling).
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §12.2/§12.3. The configured path is a <b>server-host</b> filesystem
/// path; the check executes server-side under the server process account and returns exactly the
/// typed §12.2 states (excluding <c>not-configured</c>, which the service owns when no row exists).
/// The probe never proves — and never claims — reachability from the operator's workstation or
/// browser; it creates/writes no production document (P2-T05 performs no file IO beyond the check
/// probe itself).
/// </remarks>
public interface IPdfDirectoryProbe
{
    /// <summary>Probes the server-host path and returns the typed accessibility state.</summary>
    PdfDirectoryCheckState Probe(string absoluteDirectoryPath);
}

/// <summary>
/// The <see cref="IPdfDirectoryProbe"/> over the real server-host filesystem.
/// </summary>
/// <remarks>
/// The magic-free mapping: a non-absolute path is <c>invalid-path</c>; a missing path is
/// <c>directory-not-found</c>; an existing non-directory is <c>not-a-directory</c>; read and write
/// reachability from the process account are probed (write = creating and removing a unique probe
/// file inside the directory); every other infrastructure failure is <c>check-failed</c> — never
/// conflated with <c>directory-not-found</c> (SET3/AC-F2). Unit tests exercise the same probe over
/// temp directories (SET12).
/// </remarks>
public sealed class ServerHostPdfDirectoryProbe : IPdfDirectoryProbe
{
    /// <inheritdoc />
    public PdfDirectoryCheckState Probe(string absoluteDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteDirectoryPath);

        if (!Path.IsPathRooted(absoluteDirectoryPath))
        {
            return PdfDirectoryCheckState.InvalidPath;
        }

        try
        {
            // File.Exists is consulted FIRST so a path that exists as a file is reported as
            // NotADirectory and a directory path can never be misreported: Directory.Exists is
            // false for a regular file, so checking it first would make the NotADirectory branch
            // unreachable (the typed states must never collapse into one another — SET3/AC-F2).
            if (File.Exists(absoluteDirectoryPath))
            {
                return PdfDirectoryCheckState.NotADirectory;
            }

            if (!Directory.Exists(absoluteDirectoryPath))
            {
                return PdfDirectoryCheckState.DirectoryNotFound;
            }

            var attributes = File.GetAttributes(absoluteDirectoryPath);
            if ((attributes & FileAttributes.Directory) == 0)
            {
                return PdfDirectoryCheckState.NotADirectory;
            }

            // Read reachability: enumerate at least the first entry (an empty directory still
            // yields an empty enumeration, which is a successful read probe).
            _ = Directory.EnumerateFileSystemEntries(absoluteDirectoryPath).FirstOrDefault();

            // Write reachability: create and remove a unique probe file. Nothing else is ever
            // written: P2-T05 performs no file IO beyond the check probe itself.
            var probeFile = Path.Combine(
                absoluteDirectoryPath,
                $".dmo-pdf-dir-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probeFile, string.Empty);
            File.Delete(probeFile);

            return PdfDirectoryCheckState.Ok;
        }
        catch (UnauthorizedAccessException)
        {
            return PdfDirectoryCheckState.AccessDenied;
        }
        catch (IOException)
        {
            return PdfDirectoryCheckState.AccessDenied;
        }
        catch (ArgumentException)
        {
            return PdfDirectoryCheckState.InvalidPath;
        }
        catch (NotSupportedException)
        {
            return PdfDirectoryCheckState.CheckFailed;
        }
        catch (System.Security.SecurityException)
        {
            return PdfDirectoryCheckState.AccessDenied;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return PdfDirectoryCheckState.CheckFailed;
        }
    }
}