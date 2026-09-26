using System.Reflection;
using DMO.Application.Controlo.Pesos;
using DMO.Application.Controlo.Settings;

namespace DMO.UnitTests.Controlo.Settings;

/// <summary>
/// P2-T05 unit proofs of the server-side PDF-directory probe: rows SET3 and SET12 of the
/// test-to-acceptance matrix (<c>plans/contracts/P2-T05_CONTROLO_CREATE_CONTRACT.md</c> §26.4).
/// The probe runs against the real local filesystem over temp directories and returns exactly one
/// typed §12.2 state per call — never <c>not-configured</c>, never an empty/ambiguous value, and
/// with no claim about workstation/browser reachability.
/// </summary>
public sealed class PdfDirectoryProbeTests
{
    /// <summary>
    /// SET3 (AC-F2) — the check vocabulary is distinct: an existing directory → <c>ok</c>, a
    /// missing absolute path → <c>directory-not-found</c>, a relative value → <c>invalid-path</c>.
    /// Each call returns exactly one typed member of the closed enum, and the probe itself never
    /// returns <c>not-configured</c>.
    /// <para>
    /// Known divergence (reported to the implementation owner): an existing <b>file</b> is answered
    /// <c>directory-not-found</c> by the current probe, because its <c>Directory.Exists</c>
    /// short-circuit runs before the <c>File.Exists</c> not-a-directory branch — the
    /// <c>File.Exists</c> branch is unreachable, so <c>not-a-directory</c> cannot be produced by the
    /// real filesystem probe (contract SET3/AC-F2 forbids conflating the two states). The test pins
    /// the current implementation behaviour so the divergence stays visible.</para>
    /// </summary>
    [Fact]
    public void SET3_ProbeStatesAreDistinctAndNeverConflated()
    {
        using var directory = new TempDirectory();
        var probe = new ServerHostPdfDirectoryProbe();

        // The closed state vocabulary is exactly the seven enum members; the probe owns all of
        // them except not-configured (which the CHECK service owns when no row exists).
        Assert.Equal(7, Enum.GetValues<PdfDirectoryCheckState>().Length);

        // An existing directory is ok (read and write reachable from the process account).
        Assert.Equal(PdfDirectoryCheckState.Ok, probe.Probe(directory.FullPath));

        // A missing absolute path is directory-not-found — never not-a-directory, never
        // access-denied and never check-failed.
        var missing = System.IO.Path.Combine(directory.FullPath, "missing-subfolder");
        Assert.Equal(PdfDirectoryCheckState.DirectoryNotFound, probe.Probe(missing));

        // An existing non-directory is not-a-directory: the probe consults File.Exists BEFORE
        // Directory.Exists, so a regular file can never collapse into directory-not-found and the
        // contracted state stays reachable (SET3/AC-F2 non-conflation; fixed in the probe after
        // the initial implementation flagged the short-circuit divergence).
        var filePath = System.IO.Path.Combine(directory.FullPath, "a-file.txt");
        File.WriteAllText(filePath, "not a directory");
        try
        {
            Assert.Equal(PdfDirectoryCheckState.NotADirectory, probe.Probe(filePath));
        }
        finally
        {
            File.Delete(filePath);
        }

        // A non-absolute value is invalid-path.
        Assert.Equal(PdfDirectoryCheckState.InvalidPath, probe.Probe("relative/path"));

        // A null/blank value is refused by the probe itself (no state is invented for it).
        Assert.Throws<ArgumentNullException>(() => probe.Probe(null!));
        Assert.Throws<ArgumentException>(() => probe.Probe("  "));

        // The probe never reports the service-owned state: not-configured is not a probe answer.
        Assert.NotEqual(PdfDirectoryCheckState.NotConfigured, probe.Probe(directory.FullPath));
        Assert.NotEqual(PdfDirectoryCheckState.NotConfigured, probe.Probe(missing));
        Assert.NotEqual(PdfDirectoryCheckState.NotConfigured, probe.Probe("relative/path"));
    }

    /// <summary>
    /// SET12 (AC-F2, Q-PDF) — the check is server-host semantics over the real local filesystem:
    /// after a successful probe the directory contains no leftover probe file (create and remove are
    /// the whole write probe) and stays writable; the result carrier exposes only the typed state
    /// plus an optional message — no browser/workstation reachability claim exists anywhere; and the
    /// probe contract returns exactly the typed state.
    /// </summary>
    [Fact]
    public void SET12_ProbeRunsServerSideLeavesNoDebrisAndClaimsNoWorkstationReachability()
    {
        using var directory = new TempDirectory();
        var probe = new ServerHostPdfDirectoryProbe();

        // Before the probe: the directory is empty (the test created it).
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.FullPath));

        // The probe executes against the real server-host filesystem abstraction.
        Assert.Equal(PdfDirectoryCheckState.Ok, probe.Probe(directory.FullPath));

        // After the probe: the probe file was removed again — the directory holds no leftover from
        // the check and no document was ever created.
        Assert.Equal(
            Directory.EnumerateFileSystemEntries(directory.FullPath).ToList(),
            Array.Empty<string>());

        // The directory stays writable after the check: a write still succeeds and leaves no trace.
        var afterProbe = System.IO.Path.Combine(directory.FullPath, "after-probe.tmp");
        File.WriteAllText(afterProbe, string.Empty);
        Assert.True(File.Exists(afterProbe));
        File.Delete(afterProbe);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.FullPath));

        // The result carrier is exactly the typed state plus an optional message: nothing claims
        // workstation/browser reachability, no path, no host, no credential fact.
        var resultProperties = typeof(PdfDirectoryCheckResult).GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
        Assert.Equal(
            new[] { "Message", "State" },
            resultProperties.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(typeof(PdfDirectoryCheckState), resultProperties["State"]);
        Assert.Equal(typeof(string), resultProperties["Message"]);

        var resultConstructor = Assert.Single(typeof(PdfDirectoryCheckResult).GetConstructors());
        var resultParameters = resultConstructor.GetParameters();
        Assert.Equal(2, resultParameters.Length);
        Assert.False(resultParameters[0].HasDefaultValue);
        Assert.True(resultParameters[1].HasDefaultValue);

        // The probe abstraction returns exactly the typed state — one member, one state.
        var probeMethods = typeof(IPdfDirectoryProbe).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var probeMethod = Assert.Single(probeMethods);
        Assert.Equal(typeof(PdfDirectoryCheckState), probeMethod.ReturnType);
        Assert.Equal(
            new[] { typeof(string) },
            probeMethod.GetParameters().Select(parameter => parameter.ParameterType));
    }

    /// <summary>A real temp directory that removes itself on dispose (best-effort).</summary>
    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            FullPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dmo-pdf-probe-{Guid.NewGuid():N}");
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