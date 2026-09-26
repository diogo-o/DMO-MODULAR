using DMO.Application.Controlo.Pesos;
using DMO.Application.Controlo.Settings;
using DMO.Application.Documents;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;

namespace DMO.UnitTests.Documents;

/// <summary>
/// Unit proofs of the Peso PDF orchestration: generation uses the SHARED read model (never a
/// parallel read), the configured base directory from Definições, the closed naming convention and
/// atomic storage; preconditions are typed refusals (not-configured / not-decided /
/// production-binding-missing / workspace-unavailable / invalid name / write failure); an existing
/// target is never overwritten; the Peso record is never written to (immutability of a decided
/// Peso).
/// </summary>
public sealed class PesoPdfServiceTests
{
    private static readonly Guid PesoId = new("11111111-1111-1111-1111-111111111111");
    private static readonly string BaseDirectory = Path.Combine(Path.GetTempPath(), $"dmo-peso-pdf-unit-{Guid.NewGuid():N}");

    private static PesoPdfService CreateService(
        FakePdfDirectorySettingsRepository settings,
        FakeControloRead create,
        FakePesoPdfFileStore files,
        RecordingRenderer renderer,
        IPdfDirectoryProbe? probe = null) =>
        new(settings, probe ?? new ServerHostPdfDirectoryProbe(), create, files, renderer);

    // ---- Preconditions ---------------------------------------------------------------

    [Fact]
    public async Task Generate_WithoutConfiguredDirectoryRefusesNotConfiguredAndNeverWrites()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var settings = new FakePdfDirectorySettingsRepository();
            var files = new FakePesoPdfFileStore();
            var service = CreateService(settings, new FakeControloRead(PesoPdfFixtures.DecidedSheet()), files, new RecordingRenderer());

            var result = await service.GenerateAsync(PesoId, CancellationToken.None);

            var refused = Assert.IsType<PesoPdfResult.Refused>(result);
            Assert.Equal(PesoPdfRefusalReason.PdfDirectoryNotConfigured, refused.Reason);
            Assert.Empty(files.Calls);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Generate_OnAMissingPesoReturnsNotFound()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var settings = new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), BaseDirectory, Version: 1, DateTimeOffset.UtcNow));
            var files = new FakePesoPdfFileStore();
            var service = CreateService(
                settings,
                new FakeControloRead(new PesoResult.NotFound(PesoId)),
                files,
                new RecordingRenderer());

            var result = await service.GenerateAsync(PesoId, CancellationToken.None);

            Assert.IsType<PesoPdfResult.NotFound>(result);
            Assert.Empty(files.Calls);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Generate_OnAnUndecidedPesoRefusesNotDecided()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var settings = new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), BaseDirectory, Version: 1, DateTimeOffset.UtcNow));
            var files = new FakePesoPdfFileStore();
            var service = CreateService(
                settings,
                new FakeControloRead(PesoPdfFixtures.PendingStatusSheet()),
                files,
                new RecordingRenderer());

            var result = await service.GenerateAsync(PesoId, CancellationToken.None);

            var refused = Assert.IsType<PesoPdfResult.Refused>(result);
            Assert.Equal(PesoPdfRefusalReason.NotDecided, refused.Reason);
            Assert.Empty(files.Calls);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Generate_OnAPendingAnchorPesoRefusesProductionBindingMissing()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var settings = new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), BaseDirectory, Version: 1, DateTimeOffset.UtcNow));
            var files = new FakePesoPdfFileStore();
            var service = CreateService(
                settings,
                new FakeControloRead(PesoPdfFixtures.PendingAnchorSheet()),
                files,
                new RecordingRenderer());

            var result = await service.GenerateAsync(PesoId, CancellationToken.None);

            var refused = Assert.IsType<PesoPdfResult.Refused>(result);
            Assert.Equal(PesoPdfRefusalReason.ProductionBindingMissing, refused.Reason);
            Assert.Empty(files.Calls);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Generate_WithAnInaccessibleWorkspaceRefusesWorkspaceUnavailable()
    {
        var missingBase = Path.Combine(Path.GetTempPath(), $"dmo-peso-pdf-missing-{Guid.NewGuid():N}");
        var settings = new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
            Guid.NewGuid(), missingBase, Version: 1, DateTimeOffset.UtcNow));
        var files = new FakePesoPdfFileStore();
        var service = CreateService(settings, new FakeControloRead(PesoPdfFixtures.DecidedSheet()), files, new RecordingRenderer());

        var result = await service.GenerateAsync(PesoId, CancellationToken.None);

        var refused = Assert.IsType<PesoPdfResult.Refused>(result);
        Assert.Equal(PesoPdfRefusalReason.WorkspaceUnavailable, refused.Reason);
        Assert.Empty(files.Calls);
    }

    [Fact]
    public async Task Generate_WithUnsafeTraversalFactsRefusesInvalidFileName()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var settings = new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), BaseDirectory, Version: 1, DateTimeOffset.UtcNow));
            var files = new FakePesoPdfFileStore();
            var sheet = PesoPdfFixtures.DecidedSheet() with
            {
                Production = new PesoProductionProjection("REF/X", "2026-001", "B1", null),
            };
            var service = CreateService(settings, new FakeControloRead(sheet), files, new RecordingRenderer());

            var result = await service.GenerateAsync(PesoId, CancellationToken.None);

            var refused = Assert.IsType<PesoPdfResult.Refused>(result);
            Assert.Equal(PesoPdfRefusalReason.InvalidFileName, refused.Reason);
            Assert.Empty(files.Calls);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    // ---- Generation -----------------------------------------------------------------

    [Fact]
    public async Task Generate_WritesThePdfFromTheSharedReadModelUnderTheConfiguredBase()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var sheet = PesoPdfFixtures.DecidedSheet(rowCount: 3);
            var settings = new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), BaseDirectory, Version: 1, DateTimeOffset.UtcNow));
            var files = new FakePesoPdfFileStore();
            var renderer = new RecordingRenderer();
            var service = CreateService(settings, new FakeControloRead(sheet), files, renderer);

            var result = await service.GenerateAsync(PesoId, CancellationToken.None);

            var generated = Assert.IsType<PesoPdfResult.Generated>(result);
            Assert.Equal(PesoId, generated.Output.PesoId);
            Assert.Equal(sheet.Version, generated.Output.Version);
            Assert.Equal("Peso_REF-X_B1.pdf", generated.Output.FileName);
            Assert.Equal("REF-X/2026-001/Peso_REF-X_B1.pdf", generated.Output.RelativePath);
            Assert.Equal(renderer.RenderedBytes.LongLength, generated.Output.Bytes);

            // The store received the configured base + the deterministic relative target + the
            // rendered bytes; the directory chain is created by the store.
            var call = Assert.Single(files.Calls);
            Assert.Equal(BaseDirectory, call.BaseDirectory);
            Assert.Equal("REF-X/2026-001", call.RelativeDirectory);
            Assert.Equal("Peso_REF-X_B1.pdf", call.FileName);
            Assert.Equal(renderer.RenderedBytes, call.Content);

            // The renderer received the document composed from the SHARED sheet (frozen facts).
            var model = Assert.Single(renderer.Models);
            Assert.Equal(sheet.PesoId, model.PesoId);
            Assert.Equal(sheet.Rows.Count, model.Rows.Count);
            Assert.Equal(sheet.Rows[0].CapacityCm3, model.Rows[0].CapacityCm3);
            Assert.Equal(sheet.Rows[0].GlassWeightG, model.Rows[0].GlassWeightG);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Generate_OnAnExistingTargetAnswersAlreadyAvailableAndNeverOverwrites()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var settings = new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), BaseDirectory, Version: 1, DateTimeOffset.UtcNow));
            var files = new FakePesoPdfFileStore();
            files.SeedExisting("REF-X/2026-001", "Peso_REF-X_B1.pdf", [1, 2, 3]);
            var renderer = new RecordingRenderer();
            var service = CreateService(settings, new FakeControloRead(PesoPdfFixtures.DecidedSheet()), files, renderer);

            var result = await service.GenerateAsync(PesoId, CancellationToken.None);

            var available = Assert.IsType<PesoPdfResult.AlreadyAvailable>(result);
            Assert.Equal("Peso_REF-X_B1.pdf", available.Output.FileName);
            Assert.Equal("REF-X/2026-001/Peso_REF-X_B1.pdf", available.Output.RelativePath);

            // The store refused the write (no overwrite): the existing content is untouched.
            Assert.Single(files.Calls);
            Assert.Equal("Peso_REF-X_B1.pdf", files.Calls[0].FileName);
            Assert.Equal(new byte[] { 1, 2, 3 }, files.Stored["REF-X/2026-001/Peso_REF-X_B1.pdf"]);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Generate_MapsStoreFailuresToTypedRefusals()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var settings = new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), BaseDirectory, Version: 1, DateTimeOffset.UtcNow));
            var files = new FakePesoPdfFileStore { ForcedState = PesoPdfFileWriteState.WriteFailed };
            var service = CreateService(settings, new FakeControloRead(PesoPdfFixtures.DecidedSheet()), files, new RecordingRenderer());

            var result = await service.GenerateAsync(PesoId, CancellationToken.None);

            Assert.Equal(
                PesoPdfRefusalReason.WriteFailed,
                Assert.IsType<PesoPdfResult.Refused>(result).Reason);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    // ---------------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------------

    private sealed class FakePdfDirectorySettingsRepository : IPdfDirectorySettingsRepository
    {
        private readonly PdfDirectorySettings? _settings;

        public FakePdfDirectorySettingsRepository(PdfDirectorySettings? settings = null)
        {
            _settings = settings;
        }

        public Task<PdfDirectorySettings?> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_settings);

        public Task<PdfDirectorySettings> SetAsync(
            PdfDirectorySettings settings,
            CancellationToken cancellationToken) =>
            Task.FromResult(settings);
    }

    private sealed class FakeControloRead : IControloCreateService
    {
        private readonly PesoResult _answer;

        public FakeControloRead(PesoSheetReadModel sheet) => _answer = new PesoResult.Found(sheet);

        public FakeControloRead(PesoResult answer) => _answer = answer;

        public Task<PesoResult> GetAsync(Guid pesoId, CancellationToken cancellationToken) =>
            Task.FromResult(_answer);

        public Task<PesoResult> CalculateAsync(CalculatePesoCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The documents unit tests never calculate.");

        public Task<PesoResult> CreateAsync(CreatePesoCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The documents unit tests never create.");

        public Task<PesoResult> UpdateAsync(UpdatePesoCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The documents unit tests never edit.");

        public Task<PesoResult> SubmitAsync(SubmitPesoCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The documents unit tests never submit.");

        public Task<PesoResult> AssociateAsync(AssociatePesoCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The documents unit tests never associate.");

        public Task<PesoResult> ListAssociationCandidatesAsync(Guid toolId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The documents unit tests never list candidates.");
    }

    private sealed class RecordingRenderer : IPesoPdfRenderer
    {
        public List<PesoPdfDocumentModel> Models { get; } = [];

        public byte[] RenderedBytes { get; private set; } = [];

        public byte[] Render(PesoPdfDocumentModel model, DateTimeOffset generatedAt)
        {
            Models.Add(model);
            RenderedBytes = [0x25, 0x50, 0x44, 0x46]; // "%PDF" marker bytes (content shape is irrelevant here)
            return RenderedBytes;
        }
    }

    internal sealed class FakePesoPdfFileStore : IPesoPdfFileStore
    {
        public Dictionary<string, byte[]> Stored { get; } = new(StringComparer.Ordinal);

        public List<PesoPdfFileWriteCall> Calls { get; } = [];

        public PesoPdfFileWriteState? ForcedState { get; set; }

        public void SeedExisting(string relativeDirectory, string fileName, byte[] content) =>
            Stored[$"{relativeDirectory}/{fileName}"] = content;

        public Task<PesoPdfFileWriteResult> WriteAsync(
            string baseDirectory,
            string relativeDirectory,
            string fileName,
            byte[] content,
            CancellationToken cancellationToken)
        {
            Calls.Add(new PesoPdfFileWriteCall(baseDirectory, relativeDirectory, fileName, content));

            if (ForcedState is { } forced)
            {
                return Task.FromResult(new PesoPdfFileWriteResult(forced, Bytes: 0));
            }

            var key = $"{relativeDirectory}/{fileName}";
            if (Stored.ContainsKey(key))
            {
                return Task.FromResult(new PesoPdfFileWriteResult(
                    PesoPdfFileWriteState.AlreadyExists,
                    Bytes: 0));
            }

            Stored[key] = content;
            return Task.FromResult(new PesoPdfFileWriteResult(
                PesoPdfFileWriteState.Written,
                content.LongLength));
        }

        public Task<PesoPdfFileReadResult> ReadAsync(
            string baseDirectory,
            string relativeDirectory,
            string fileName,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The generation unit tests never read stored files.");

        public Task<PesoPdfFilePresenceResult> ExistsAsync(
            string baseDirectory,
            string relativeDirectory,
            string fileName,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The generation unit tests never check stored files.");
    }

    internal sealed record PesoPdfFileWriteCall(
        string BaseDirectory,
        string RelativeDirectory,
        string FileName,
        byte[] Content);
}