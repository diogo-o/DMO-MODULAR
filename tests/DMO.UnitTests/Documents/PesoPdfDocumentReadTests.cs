using DMO.Application.Controlo.Pesos;
using DMO.Application.Controlo.Settings;
using DMO.Application.Documents;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;

namespace DMO.UnitTests.Documents;

/// <summary>
/// Unit proofs of the Peso PDF document read (the outputs slice): the availability and content reads
/// resolve the deterministic target from the SHARED P2-T05 read model and the configured base
/// directory, read the file FROM the configured base directory only, and answer the typed states —
/// <c>Available</c>/<c>Found</c> for an existing file, <c>NotGenerated</c> for a missing file (never
/// an error), <c>NotFound</c> for a missing Peso and the typed refusals for every
/// workspace/configuration/infrastructure condition, never conflated with a missing file.
/// </summary>
public sealed class PesoPdfDocumentReadTests
{
    private static readonly Guid PesoId = new("11111111-1111-1111-1111-111111111111");
    private static readonly string BaseDirectory =
        Path.Combine(Path.GetTempPath(), $"dmo-peso-pdf-read-{Guid.NewGuid():N}");

    private static PesoPdfDocumentReadService CreateService(
        FakePdfDirectorySettingsRepository settings,
        FakeControloRead create,
        IPesoPdfFileStore files,
        IPdfDirectoryProbe? probe = null) =>
        new(settings, probe ?? new ServerHostPdfDirectoryProbe(), create, files);

    // ---- Availability ---------------------------------------------------------------

    [Fact]
    public async Task Availability_OnAMissingFileIsNotGeneratedAndNeverReadsBytes()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var settings = new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), BaseDirectory, Version: 1, DateTimeOffset.UtcNow));
            var files = new FakePesoPdfFileStore();
            var service = CreateService(settings, new FakeControloRead(PesoPdfFixtures.DecidedSheet()), files);

            var result = await service.GetAvailabilityAsync(PesoId, CancellationToken.None);

            Assert.IsType<PesoPdfAvailabilityResult.NotGenerated>(result);
            Assert.Single(files.ExistsCalls);
            Assert.Empty(files.ReadCalls);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Availability_OnAnExistingFileIsAvailableWithTheDeterministicFileName()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var settings = new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), BaseDirectory, Version: 1, DateTimeOffset.UtcNow));
            var files = new FakePesoPdfFileStore();
            files.SeedExisting("REF-X/2026-001", "Peso_REF-X_B1.pdf", [1, 2, 3]);
            var service = CreateService(settings, new FakeControloRead(PesoPdfFixtures.DecidedSheet()), files);

            var result = await service.GetAvailabilityAsync(PesoId, CancellationToken.None);

            var available = Assert.IsType<PesoPdfAvailabilityResult.Available>(result);
            Assert.Equal("Peso_REF-X_B1.pdf", available.FileName);
            Assert.Equal("REF-X/2026-001/Peso_REF-X_B1.pdf", available.RelativePath);

            // The check went to the CONFIGURED base directory and the deterministic target only.
            var call = Assert.Single(files.ExistsCalls);
            Assert.Equal(BaseDirectory, call.BaseDirectory);
            Assert.Equal("REF-X/2026-001", call.RelativeDirectory);
            Assert.Equal("Peso_REF-X_B1.pdf", call.FileName);
            Assert.Empty(files.ReadCalls);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Availability_WithoutConfiguredDirectoryRefusesNotConfigured()
    {
        var settings = new FakePdfDirectorySettingsRepository();
        var files = new FakePesoPdfFileStore();
        var service = CreateService(settings, new FakeControloRead(PesoPdfFixtures.DecidedSheet()), files);

        var result = await service.GetAvailabilityAsync(PesoId, CancellationToken.None);

        var refused = Assert.IsType<PesoPdfAvailabilityResult.Refused>(result);
        Assert.Equal(PesoPdfDocumentReadRefusalReason.PdfDirectoryNotConfigured, refused.Reason);
        Assert.Empty(files.ExistsCalls);
    }

    [Fact]
    public async Task Availability_OnAnInaccessibleWorkspaceRefusesWorkspaceUnavailable()
    {
        var missingBase = Path.Combine(Path.GetTempPath(), $"dmo-peso-pdf-missing-{Guid.NewGuid():N}");
        var settings = new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
            Guid.NewGuid(), missingBase, Version: 1, DateTimeOffset.UtcNow));
        var files = new FakePesoPdfFileStore();
        var service = CreateService(settings, new FakeControloRead(PesoPdfFixtures.DecidedSheet()), files);

        var result = await service.GetAvailabilityAsync(PesoId, CancellationToken.None);

        var refused = Assert.IsType<PesoPdfAvailabilityResult.Refused>(result);
        Assert.Equal(PesoPdfDocumentReadRefusalReason.WorkspaceUnavailable, refused.Reason);
        Assert.Empty(files.ExistsCalls);
    }

    [Fact]
    public async Task Availability_OnAPendingAnchorPesoRefusesProductionBindingMissing()
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
                files);

            var result = await service.GetAvailabilityAsync(PesoId, CancellationToken.None);

            var refused = Assert.IsType<PesoPdfAvailabilityResult.Refused>(result);
            Assert.Equal(PesoPdfDocumentReadRefusalReason.ProductionBindingMissing, refused.Reason);
            Assert.Empty(files.ExistsCalls);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Availability_OnAMissingPesoIsNotFound()
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
                files);

            var result = await service.GetAvailabilityAsync(PesoId, CancellationToken.None);

            var notFound = Assert.IsType<PesoPdfAvailabilityResult.NotFound>(result);
            Assert.Equal(PesoId, notFound.PesoId);
            Assert.Empty(files.ExistsCalls);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Availability_OnAPresenceCheckFailureRefusesReadFailed()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var settings = new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), BaseDirectory, Version: 1, DateTimeOffset.UtcNow));
            var files = new FakePesoPdfFileStore { ForcedExistsState = PesoPdfFilePresenceState.Failed };
            var service = CreateService(settings, new FakeControloRead(PesoPdfFixtures.DecidedSheet()), files);

            var result = await service.GetAvailabilityAsync(PesoId, CancellationToken.None);

            var refused = Assert.IsType<PesoPdfAvailabilityResult.Refused>(result);
            Assert.Equal(PesoPdfDocumentReadRefusalReason.ReadFailed, refused.Reason);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    // ---- Content read ----------------------------------------------------------------

    [Fact]
    public async Task Read_ReturnsTheStoredBytesFromTheConfiguredBaseDirectory()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var settings = new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), BaseDirectory, Version: 1, DateTimeOffset.UtcNow));
            byte[] expected = [0x25, 0x50, 0x44, 0x46, 0x01, 0x02];
            var files = new FakePesoPdfFileStore();
            files.SeedExisting("REF-X/2026-001", "Peso_REF-X_B1.pdf", expected);
            var service = CreateService(settings, new FakeControloRead(PesoPdfFixtures.DecidedSheet()), files);

            var result = await service.ReadAsync(PesoId, CancellationToken.None);

            var found = Assert.IsType<PesoPdfContentResult.Found>(result);
            Assert.Equal("Peso_REF-X_B1.pdf", found.FileName);
            Assert.Equal("REF-X/2026-001/Peso_REF-X_B1.pdf", found.RelativePath);
            Assert.Equal(expected, found.Content);

            // The read went to the CONFIGURED base directory — never to a hardcoded location.
            var call = Assert.Single(files.ReadCalls);
            Assert.Equal(BaseDirectory, call.BaseDirectory);
            Assert.Equal("REF-X/2026-001", call.RelativeDirectory);
            Assert.Equal("Peso_REF-X_B1.pdf", call.FileName);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Read_OnAMissingFileIsNotGenerated()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var settings = new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), BaseDirectory, Version: 1, DateTimeOffset.UtcNow));
            var files = new FakePesoPdfFileStore();
            var service = CreateService(settings, new FakeControloRead(PesoPdfFixtures.DecidedSheet()), files);

            var result = await service.ReadAsync(PesoId, CancellationToken.None);

            Assert.IsType<PesoPdfContentResult.NotGenerated>(result);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Read_WithoutConfiguredDirectoryRefusesNotConfigured()
    {
        var settings = new FakePdfDirectorySettingsRepository();
        var files = new FakePesoPdfFileStore();
        var service = CreateService(settings, new FakeControloRead(PesoPdfFixtures.DecidedSheet()), files);

        var result = await service.ReadAsync(PesoId, CancellationToken.None);

        var refused = Assert.IsType<PesoPdfContentResult.Refused>(result);
        Assert.Equal(PesoPdfDocumentReadRefusalReason.PdfDirectoryNotConfigured, refused.Reason);
        Assert.Empty(files.ReadCalls);
    }

    [Fact]
    public async Task Read_OnAReadFailureRefusesReadFailed()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var settings = new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), BaseDirectory, Version: 1, DateTimeOffset.UtcNow));
            var files = new FakePesoPdfFileStore { ForcedReadState = PesoPdfFileReadState.Failed };
            var service = CreateService(settings, new FakeControloRead(PesoPdfFixtures.DecidedSheet()), files);

            var result = await service.ReadAsync(PesoId, CancellationToken.None);

            var refused = Assert.IsType<PesoPdfContentResult.Refused>(result);
            Assert.Equal(PesoPdfDocumentReadRefusalReason.ReadFailed, refused.Reason);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    // ---- The REAL server-host store: the file is read from the configured base --------

    [Fact]
    public async Task Read_WithTheRealStoreReadsTheFileFromTheConfiguredBase()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            // Arrange the real deterministic target under the configured base directory.
            byte[] expected = [0x25, 0x50, 0x44, 0x46, 0x0A, 0x0B, 0x0C];
            var store = new ServerHostPesoPdfFileStore();
            await store.WriteAsync(
                BaseDirectory, "REF-X/2026-001", "Peso_REF-X_B1.pdf", expected, CancellationToken.None);

            var settings = new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), BaseDirectory, Version: 1, DateTimeOffset.UtcNow));
            var service = CreateService(
                settings,
                new FakeControloRead(PesoPdfFixtures.DecidedSheet()),
                store);

            var found = Assert.IsType<PesoPdfContentResult.Found>(
                await service.ReadAsync(PesoId, CancellationToken.None));
            Assert.Equal(expected, found.Content);
            Assert.Equal("Peso_REF-X_B1.pdf", found.FileName);
            Assert.Equal("REF-X/2026-001/Peso_REF-X_B1.pdf", found.RelativePath);

            // The physical file is exactly the configured-base-relative one — no absolute path was
            // minted or exposed anywhere.
            Assert.True(File.Exists(Path.Combine(BaseDirectory, "REF-X", "2026-001", "Peso_REF-X_B1.pdf")));
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
            throw new NotSupportedException("The document-read unit tests never calculate.");

        public Task<PesoResult> CreateAsync(CreatePesoCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The document-read unit tests never create.");

        public Task<PesoResult> UpdateAsync(UpdatePesoCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The document-read unit tests never edit.");

        public Task<PesoResult> SubmitAsync(SubmitPesoCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The document-read unit tests never submit.");

        public Task<PesoResult> AssociateAsync(AssociatePesoCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The document-read unit tests never associate.");

        public Task<PesoResult> ListAssociationCandidatesAsync(Guid toolId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The document-read unit tests never list candidates.");
    }

    internal sealed class FakePesoPdfFileStore : IPesoPdfFileStore
    {
        private readonly Dictionary<string, byte[]> _stored = new(StringComparer.Ordinal);

        public List<PesoPdfFileReadCall> ExistsCalls { get; } = [];

        public List<PesoPdfFileReadCall> ReadCalls { get; } = [];

        public PesoPdfFilePresenceState? ForcedExistsState { get; set; }

        public PesoPdfFileReadState? ForcedReadState { get; set; }

        public void SeedExisting(string relativeDirectory, string fileName, byte[] content) =>
            _stored[$"{relativeDirectory}/{fileName}"] = content;

        public Task<PesoPdfFileWriteResult> WriteAsync(
            string baseDirectory,
            string relativeDirectory,
            string fileName,
            byte[] content,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The document-read unit tests never write.");

        public Task<PesoPdfFileReadResult> ReadAsync(
            string baseDirectory,
            string relativeDirectory,
            string fileName,
            CancellationToken cancellationToken)
        {
            ReadCalls.Add(new PesoPdfFileReadCall(baseDirectory, relativeDirectory, fileName));

            if (ForcedReadState is { } forced)
            {
                return Task.FromResult(new PesoPdfFileReadResult(forced, null));
            }

            return Task.FromResult(_stored.TryGetValue($"{relativeDirectory}/{fileName}", out var bytes)
                ? PesoPdfFileReadResult.Found(bytes)
                : PesoPdfFileReadResult.Missing());
        }

        public Task<PesoPdfFilePresenceResult> ExistsAsync(
            string baseDirectory,
            string relativeDirectory,
            string fileName,
            CancellationToken cancellationToken)
        {
            ExistsCalls.Add(new PesoPdfFileReadCall(baseDirectory, relativeDirectory, fileName));

            if (ForcedExistsState is { } forced)
            {
                return Task.FromResult(new PesoPdfFilePresenceResult(forced));
            }

            return Task.FromResult(_stored.ContainsKey($"{relativeDirectory}/{fileName}")
                ? PesoPdfFilePresenceResult.Found()
                : PesoPdfFilePresenceResult.Missing());
        }
    }

    internal sealed record PesoPdfFileReadCall(
        string BaseDirectory,
        string RelativeDirectory,
        string FileName);
}