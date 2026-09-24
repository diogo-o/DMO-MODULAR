using DMO.Application.Documents;

namespace DMO.UnitTests.Documents;

/// <summary>
/// Unit proofs of the server-host Peso PDF file store over real temp directories: the
/// <c>&lt;base&gt;/&lt;reference&gt;/&lt;production-number&gt;/</c> chain is created automatically
/// (the operator is never asked to create directories), the write is atomic (no temp debris), an
/// existing target is refused without ever being overwritten, and workspace conditions are typed
/// and never conflated with a missing file.
/// </summary>
public sealed class ServerHostPesoPdfFileStoreTests
{
    private readonly ServerHostPesoPdfFileStore _store = new();

    [Fact]
    public async Task Write_CreatesTheReferenceAndProductionDirectoriesAndStoresTheFile()
    {
        using var directory = new TempDirectory();
        var content = "PDF-BYTES"u8.ToArray();

        var result = await _store.WriteAsync(
            directory.FullPath,
            "REF-A/2026-001",
            "Peso_REF-A_B1.pdf",
            content,
            CancellationToken.None);

        Assert.Equal(PesoPdfFileWriteState.Written, result.State);
        Assert.Equal(content.Length, result.Bytes);

        var target = Path.Combine(directory.FullPath, "REF-A", "2026-001", "Peso_REF-A_B1.pdf");
        Assert.True(File.Exists(target));
        Assert.Equal(content, File.ReadAllBytes(target));

        // The automatic directory chain exists and holds exactly the written file (no temp debris).
        Assert.Equal(
            new[] { "Peso_REF-A_B1.pdf" },
            Directory.EnumerateFiles(Path.Combine(directory.FullPath, "REF-A", "2026-001"))
                .Select(path => Path.GetFileName(path))
                .ToArray());
    }

    [Fact]
    public async Task Write_RefusesAnExistingTargetAndNeverOverwrites()
    {
        using var directory = new TempDirectory();
        var original = "ORIGINAL"u8.ToArray();
        var replacement = "REPLACEMENT"u8.ToArray();

        var first = await _store.WriteAsync(
            directory.FullPath, "REF-A/2026-001", "Peso_REF-A_B1.pdf", original, CancellationToken.None);
        Assert.Equal(PesoPdfFileWriteState.Written, first.State);

        var second = await _store.WriteAsync(
            directory.FullPath, "REF-A/2026-001", "Peso_REF-A_B1.pdf", replacement, CancellationToken.None);

        Assert.Equal(PesoPdfFileWriteState.AlreadyExists, second.State);

        var target = Path.Combine(directory.FullPath, "REF-A", "2026-001", "Peso_REF-A_B1.pdf");
        Assert.Equal(original, File.ReadAllBytes(target));

        // No temporary file debris anywhere in the chain.
        Assert.DoesNotContain(
            Directory.EnumerateFiles(directory.FullPath, "*", SearchOption.AllDirectories),
            path => !path.EndsWith("Peso_REF-A_B1.pdf", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("missing-base")]
    public async Task Write_WithAMissingOrRelativeBaseAnswersWorkspaceUnavailable(string baseValue)
    {
        var result = await _store.WriteAsync(
            baseValue, "REF-A/2026-001", "Peso_REF-A_B1.pdf", [1], CancellationToken.None);

        Assert.Equal(PesoPdfFileWriteState.WorkspaceUnavailable, result.State);
    }

    [Fact]
    public async Task Write_WithAnExistingFileAsBaseAnswersWorkspaceUnavailable()
    {
        using var directory = new TempDirectory();
        var fileAsBase = Path.Combine(directory.FullPath, "not-a-directory.txt");
        File.WriteAllText(fileAsBase, "file");

        var result = await _store.WriteAsync(
            fileAsBase, "REF-A/2026-001", "Peso_REF-A_B1.pdf", [1], CancellationToken.None);

        Assert.Equal(PesoPdfFileWriteState.WorkspaceUnavailable, result.State);
    }

    [Fact]
    public async Task Write_WithABlankBaseIsRefusedByTheContract()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.WriteAsync(
            "   ", "REF-A/2026-001", "Peso_REF-A_B1.pdf", [1], CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.WriteAsync(
            null!, "REF-A/2026-001", "Peso_REF-A_B1.pdf", [1], CancellationToken.None));
    }

    /// <summary>A real temp directory that removes itself on dispose (best-effort).</summary>
    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            FullPath = Path.Combine(
                Path.GetTempPath(),
                $"dmo-peso-pdf-store-{Guid.NewGuid():N}");
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