using DMO.Application.ControloCreate;
using DMO.Application.Documents;
using DMO.Application.JobOn;
using DMO.Application.Repositories;
using DMO.Domain.JobOn;
using DMO.Domain.Tools;
using DomainJobOn = DMO.Domain.JobOn.JobOn;
using DomainJobOnId = DMO.Domain.JobOn.JobOnId;

namespace DMO.UnitTests.JobOn;

/// <summary>
/// Unit proofs of the Job On control-outputs read (this slice): the projection composes the
/// occurrence, the related Pesos (through the Controlo-owned read seam) and the Peso PDF
/// availability/content (through the Documents seam); a Peso with a PDF is available, a Peso
/// without one is not-generated (never an error), no related Peso means no output at all, and the
/// open read enforces the related-peso scope (a Peso of another production is never served).
/// The service itself never touches the filesystem and never exposes a path — every consequence is
/// delegated to the Documents seam.
/// </summary>
public sealed class JobOnControlOutputsTests
{
    private static readonly Guid JobOnId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PesoAvailable = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PesoNotGenerated = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ForeignPeso = new("44444444-4444-4444-4444-444444444444");

    private static JobOnControlOutputsService CreateService(
        IJobOnRepository jobOns,
        IPesoOutputRead? outputs = null,
        IPesoPdfDocumentRead? documents = null) =>
        new(
            jobOns,
            outputs ?? new FakeOutputRead(JobOnId, [PesoAvailable, PesoNotGenerated]),
            documents ?? new FakeDocumentRead(foundFor: PesoAvailable, content: [0x25, 0x50, 0x44, 0x46]));

    // ---------------------------------------------------------------------------------
    // Projection
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Projection_WithAnAvailablePesoAndANotGeneratedPesoMapsBothStates()
    {
        var store = new FakeJobOnRepository();
        SeedJobOn(store, JobOnId);
        var service = CreateService(store);

        var result = await service.GetAsync(JobOnId, CancellationToken.None);

        var found = Assert.IsType<JobOnControlOutputsResult.Found>(result);
        var outputs = Assert.IsType<JobOnControlOutputs>(found.Value).Outputs;
        Assert.Equal(2, outputs.Count);

        Assert.Equal(PesoAvailable, outputs[0].PesoId);
        Assert.Equal(PesoOutputAvailability.Available, outputs[0].Availability);
        Assert.Equal("Peso_REF-X_B1.pdf", outputs[0].FileName);

        Assert.Equal(PesoNotGenerated, outputs[1].PesoId);
        Assert.Equal(PesoOutputAvailability.NotGenerated, outputs[1].Availability);
        Assert.Null(outputs[1].FileName);
    }

    [Fact]
    public async Task Projection_WithoutRelatedPesosHasNoOutputs()
    {
        var store = new FakeJobOnRepository();
        SeedJobOn(store, JobOnId);
        var service = CreateService(store, new FakeOutputRead(JobOnId, []));

        var result = await service.GetAsync(JobOnId, CancellationToken.None);

        var found = Assert.IsType<JobOnControlOutputsResult.Found>(result);
        Assert.Empty(Assert.IsType<JobOnControlOutputs>(found.Value).Outputs);
    }

    [Fact]
    public async Task Projection_OnAMissingOccurrenceIsNotFound()
    {
        var store = new FakeJobOnRepository();
        var service = CreateService(store);

        var result = await service.GetAsync(JobOnId, CancellationToken.None);

        Assert.IsType<JobOnControlOutputsResult.NotFound>(result);
    }

    /// <summary>
    /// The P2-T08 availability rule on the projection: a missing/not-configured workspace maps to
    /// <c>WorkspaceUnavailable</c> — never conflated with a missing file — while an infrastructure
    /// refusal that says nothing about the workspace maps truthfully to not-generated (there is no
    /// file to open either way, and the projection invents nothing).
    /// </summary>
    [Fact]
    public async Task Projection_MapsWorkspaceRefusalsDistinctlyFromOtherRefusals()
    {
        var store = new FakeJobOnRepository();
        SeedJobOn(store, JobOnId);
        var workspacePeso = Guid.NewGuid();
        var infraPeso = Guid.NewGuid();
        var documents = new FakeDocumentRead();
        documents.SetAvailability(workspacePeso, new PesoPdfAvailabilityResult.Refused(
            PesoPdfDocumentReadRefusalReason.PdfDirectoryNotConfigured,
            "não configurado"));
        documents.SetAvailability(infraPeso, new PesoPdfAvailabilityResult.Refused(
            PesoPdfDocumentReadRefusalReason.ReadFailed,
            "falha de leitura"));
        var service = CreateService(
            store,
            new FakeOutputRead(JobOnId, [workspacePeso, infraPeso]),
            documents);

        var result = await service.GetAsync(JobOnId, CancellationToken.None);

        var outputs = Assert.IsType<JobOnControlOutputsResult.Found>(result).Value.Outputs;
        Assert.Equal(2, outputs.Count);
        Assert.Equal(PesoOutputAvailability.WorkspaceUnavailable, outputs[0].Availability);
        Assert.Equal(PesoOutputAvailability.NotGenerated, outputs[1].Availability);
    }

    /// <summary>A related Peso that vanished between the relation read and the document read is
    /// dropped: the projection never invents an output for a Peso that no longer exists.</summary>
    [Fact]
    public async Task Projection_DropsAPesoTheDocumentReadNoLongerFinds()
    {
        var store = new FakeJobOnRepository();
        SeedJobOn(store, JobOnId);
        var vanished = Guid.NewGuid();
        var documents = new FakeDocumentRead();
        documents.SetAvailability(vanished, new PesoPdfAvailabilityResult.NotFound(vanished));
        var service = CreateService(store, new FakeOutputRead(JobOnId, [vanished]), documents);

        var result = await service.GetAsync(JobOnId, CancellationToken.None);

        var outputs = Assert.IsType<JobOnControlOutputsResult.Found>(result).Value.Outputs;
        Assert.Empty(outputs);
    }

    /// <summary>The projection preserves the stable order of the Controlo read (no ranking, no
    /// merging and no reordering).</summary>
    [Fact]
    public async Task Projection_PreservesTheOrderOfTheRelatedPesosRead()
    {
        var store = new FakeJobOnRepository();
        SeedJobOn(store, JobOnId);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var service = CreateService(store, new FakeOutputRead(JobOnId, [first, second]));

        var result = await service.GetAsync(JobOnId, CancellationToken.None);

        var outputs = Assert.IsType<JobOnControlOutputsResult.Found>(result).Value.Outputs;
        Assert.Equal(new[] { first, second }, outputs.Select(output => output.PesoId));
    }

    // ---------------------------------------------------------------------------------
    // Open read (related-peso scoped content)
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Read_ReturnsTheStoredBytesOfARelatedPeso()
    {
        var store = new FakeJobOnRepository();
        SeedJobOn(store, JobOnId);
        var service = CreateService(store, documents: new FakeDocumentRead(
            foundFor: PesoAvailable,
            content: [0x25, 0x50, 0x44, 0x46, 0x01]));

        var result = await service.ReadAsync(JobOnId, PesoAvailable, CancellationToken.None);

        var found = Assert.IsType<JobOnControlOutputContentResult.Found>(result);
        Assert.Equal("Peso_REF-X_B1.pdf", found.FileName);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x01 }, found.Content);
    }

    [Fact]
    public async Task Read_OnARelatedPesoWhoseFileIsMissingIsNotGenerated()
    {
        var store = new FakeJobOnRepository();
        SeedJobOn(store, JobOnId);
        var service = CreateService(store);

        var result = await service.ReadAsync(JobOnId, PesoNotGenerated, CancellationToken.None);

        Assert.IsType<JobOnControlOutputContentResult.NotGenerated>(result);
    }

    [Fact]
    public async Task Read_OnANonRelatedPesoIsRefusedAndNeverServed()
    {
        var store = new FakeJobOnRepository();
        SeedJobOn(store, JobOnId);
        var service = CreateService(store, documents: new FakeDocumentRead(
            foundFor: ForeignPeso,
            content: [1, 2, 3]));

        var result = await service.ReadAsync(JobOnId, ForeignPeso, CancellationToken.None);

        var refused = Assert.IsType<JobOnControlOutputContentResult.RelatedPesoNotFound>(result);
        Assert.Equal(JobOnId, refused.JobOnId);
        Assert.Equal(ForeignPeso, refused.PesoId);
    }

    [Fact]
    public async Task Read_OnAMissingOccurrenceIsNotFound()
    {
        var service = CreateService(new FakeJobOnRepository());

        var result = await service.ReadAsync(JobOnId, PesoAvailable, CancellationToken.None);

        Assert.IsType<JobOnControlOutputContentResult.NotFound>(result);
    }

    /// <summary>The open read maps the Documents refusals to the Job On refusal vocabulary and
    /// maps the (unreachable-for-a-related-Peso) missing-binding refusal truthfully to
    /// not-generated.</summary>
    [Fact]
    public async Task Read_MapsDocumentRefusalsToTheOpenRefusalVocabulary()
    {
        var store = new FakeJobOnRepository();
        SeedJobOn(store, JobOnId);
        var documents = new FakeDocumentRead();
        documents.SetContent(PesoAvailable, new PesoPdfContentResult.Refused(
            PesoPdfDocumentReadRefusalReason.WorkspaceUnavailable,
            "workspace indisponível"));
        var service = CreateService(store, documents: documents);

        var refused = Assert.IsType<JobOnControlOutputContentResult.Refused>(
            await service.ReadAsync(JobOnId, PesoAvailable, CancellationToken.None));
        Assert.Equal(JobOnControlOutputRefusalReason.WorkspaceUnavailable, refused.Reason);
        Assert.False(string.IsNullOrWhiteSpace(refused.Message));
    }

    [Fact]
    public async Task Read_MapsAMissingBindingRefusalToNotGenerated()
    {
        var store = new FakeJobOnRepository();
        SeedJobOn(store, JobOnId);
        var documents = new FakeDocumentRead();
        documents.SetContent(PesoAvailable, new PesoPdfContentResult.Refused(
            PesoPdfDocumentReadRefusalReason.ProductionBindingMissing,
            "sem associação"));
        var service = CreateService(store, documents: documents);

        var result = await service.ReadAsync(JobOnId, PesoAvailable, CancellationToken.None);

        Assert.IsType<JobOnControlOutputContentResult.NotGenerated>(result);
    }

    // ---------------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------------

    private static void SeedJobOn(FakeJobOnRepository store, Guid jobOnId) =>
        store.Add(new DomainJobOn(
            DomainJobOnId.From(jobOnId),
            "REF-X",
            "2026-001",
            MachineCode.From("B1"),
            new DateOnly(2026, 9, 19),
            CopiedFromJobOnId: null,
            Version: 1,
            Contexts: []));

    private sealed class FakeJobOnRepository : IJobOnRepository
    {
        private readonly Dictionary<Guid, DomainJobOn> _jobOns = [];

        public void Add(DomainJobOn jobOn) => _jobOns[jobOn.JobOnId.Value] = jobOn;

        public Task<DomainJobOn?> GetByIdAsync(Guid jobOnId, CancellationToken cancellationToken) =>
            Task.FromResult(_jobOns.TryGetValue(jobOnId, out var jobOn) ? jobOn : null);

        public Task<DomainJobOn?> FindByProductionAsync(
            string reference,
            string productionNumber,
            CancellationToken cancellationToken) =>
            Task.FromResult(_jobOns.Values.FirstOrDefault(jobOn =>
                jobOn.Reference == reference && jobOn.ProductionNumber == productionNumber));

        public Task<IReadOnlyList<JobOnProductionListItem>> ListByReferenceAsync(
            string reference,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobOnProductionListItem>>(
                _jobOns.Values
                    .Where(jobOn => jobOn.Reference == reference)
                    .Select(jobOn => new JobOnProductionListItem(
                        jobOn.JobOnId.Value,
                        jobOn.Reference,
                        jobOn.ProductionNumber,
                        jobOn.Machine.Value,
                        jobOn.ProductionDate))
                    .ToList());

        public Task<DomainJobOn> CreatedAsync(
            DomainJobOn jobOn,
            IReadOnlyList<ToolContext> contexts,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The outputs unit tests never create.");

        public Task<DomainJobOn> UpdatedAsync(
            DomainJobOn jobOn,
            IReadOnlyList<ToolContextChange> changes,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The outputs unit tests never edit.");

        public Task<DomainJobOn> DuplicatedAsync(
            DomainJobOn duplicate,
            IReadOnlyList<ToolContext> duplicatedContexts,
            int expectedSourceVersion,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The outputs unit tests never duplicate.");

        public Task DeletedAsync(Guid jobOnId, int expectedVersion, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The outputs unit tests never delete.");

        public Task<IReadOnlyList<JobOnDependency>> ListLineageDependentsAsync(
            Guid jobOnId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobOnDependency>>([]);
    }

    private sealed class FakeOutputRead : IPesoOutputRead
    {
        private readonly Guid _jobOnId;
        private readonly IReadOnlyList<Guid> _pesos;

        public FakeOutputRead(Guid jobOnId, IReadOnlyList<Guid> pesos)
        {
            _jobOnId = jobOnId;
            _pesos = pesos;
        }

        public Task<IReadOnlyList<Guid>> ListByJobOnIdAsync(
            Guid jobOnId,
            CancellationToken cancellationToken) =>
            Task.FromResult(jobOnId == _jobOnId ? _pesos : (IReadOnlyList<Guid>)[]);
    }

    private sealed class FakeDocumentRead : IPesoPdfDocumentRead
    {
        private readonly Guid? _foundFor;
        private readonly byte[]? _content;
        private readonly Dictionary<Guid, PesoPdfAvailabilityResult> _availabilityOverrides = [];
        private readonly Dictionary<Guid, PesoPdfContentResult> _contentOverrides = [];

        public FakeDocumentRead(Guid? foundFor = null, byte[]? content = null)
        {
            _foundFor = foundFor;
            _content = content;
        }

        public void SetAvailability(Guid pesoId, PesoPdfAvailabilityResult result) =>
            _availabilityOverrides[pesoId] = result;

        public void SetContent(Guid pesoId, PesoPdfContentResult result) =>
            _contentOverrides[pesoId] = result;

        public Task<PesoPdfAvailabilityResult> GetAvailabilityAsync(
            Guid pesoId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_availabilityOverrides.TryGetValue(pesoId, out var overridden)
                ? overridden
                : pesoId == _foundFor
                    ? new PesoPdfAvailabilityResult.Available("Peso_REF-X_B1.pdf", "REF-X/2026-001/Peso_REF-X_B1.pdf")
                    : new PesoPdfAvailabilityResult.NotGenerated());

        public Task<PesoPdfContentResult> ReadAsync(
            Guid pesoId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_contentOverrides.TryGetValue(pesoId, out var overridden)
                ? overridden
                : pesoId == _foundFor
                    ? new PesoPdfContentResult.Found("Peso_REF-X_B1.pdf", "REF-X/2026-001/Peso_REF-X_B1.pdf", _content!)
                    : new PesoPdfContentResult.NotGenerated());
    }
}
