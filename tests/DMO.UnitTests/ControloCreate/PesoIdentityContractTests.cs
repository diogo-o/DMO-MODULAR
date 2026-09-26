using System.Reflection;
using DMO.Application.Controlo.Pesos;
using DMO.Application.JobOn;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Controlo;
using DMO.Domain.JobOn;
using DMO.Domain.Tools;
using DMO.Web.Endpoints.Controlo;
using DomainJobOn = DMO.Domain.JobOn.JobOn;

namespace DMO.UnitTests.ControloCreate;

/// <summary>
/// P2-T05 unit proofs of the Peso identity contract: rows PID4 and PID9 of the test-to-acceptance
/// matrix (<c>plans/contracts/P2-T05_CONTROLO_CREATE_CONTRACT.md</c> §26.4). No create/calculate
/// carrier carries a client-supplied Peso identity, and the association candidates are real
/// <c>cm_contexts</c> rows resolving to the anchor Tool — never synthesized, ranked or inferred.
/// </summary>
public sealed class PesoIdentityContractTests
{
    private static readonly BindingFlags DeclaredInstance =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    /// <summary>
    /// PID4 (AC-P4) — <c>peso_id</c> appears in no create/calculate command or request as a
    /// client-supplied value: the command and request carriers declare no <c>PesoId</c> member and
    /// their only Guid-typed members are the two accepted anchors (<c>cm_id</c>/<c>pending_tool_id</c>)
    /// plus — on the application create command only — the backend-resolved actor
    /// (<c>CreatedByUserId</c> is set by the Web layer from the current account, never supplied by
    /// the client and never present on a transport request); the row carrier carries no identity at
    /// all; the create <b>response</b> — and only the response — returns the backend-allocated
    /// identity.
    /// </summary>
    [Fact]
    public void PID4_NoCreateOrCalculateCarrierCarriesAClientSuppliedPesoIdentity()
    {
        // The four create/calculate carriers of the whole trip: the two application commands and the
        // two transport requests. The allowed Guid-typed members are exactly the contracted anchors,
        // plus the backend actor on the create command (which the transport never carries).
        var allowedGuidMembers = new Dictionary<Type, string[]>
        {
            [typeof(CalculatePesoCommand)] = ["CmId", "PendingToolId"],
            [typeof(CreatePesoCommand)] = ["CmId", "PendingToolId", "CreatedByUserId"],
            [typeof(ControloCreateEndpoints.CreatePesoRequest)] = ["CmId", "PendingToolId"],
            [typeof(ControloCreateEndpoints.CalculatePesoRequest)] = ["CmId", "PendingToolId"],
        };

        foreach (var (carrier, allowed) in allowedGuidMembers)
        {
            var properties = carrier.GetProperties(DeclaredInstance);

            // No member is even spelled as a Peso identity.
            Assert.DoesNotContain(
                properties,
                property => property.Name.Contains("PesoId", StringComparison.OrdinalIgnoreCase));

            // No identity-typed member exists beyond the allowed set: no "Id" member, no row id, no
            // client-minted identity of any other kind.
            Assert.All(
                properties.Where(property => property.PropertyType == typeof(Guid) ||
                                             property.PropertyType == typeof(Guid?)),
                property => Assert.Contains(property.Name, allowed, StringComparer.Ordinal));
        }

        // The two application commands declare exactly the eight contracted create facts minus the
        // identity: no PesoId anywhere on CreatePesoCommand (the check above already proves it, and
        // this pins the exact member surface).
        Assert.Equal(
            new[]
            {
                "CmId", "CreatedByUserId", "PendingToolId", "PreviousAverageWeightReference",
                "PreviousProductionEndReference", "RowWaterWeightsG", "VolumeMarisaBq",
                "VolumePuncaoPu", "WaterTemperature",
            },
            typeof(CreatePesoCommand).GetProperties(DeclaredInstance)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));

        // The measurement-row carrier transports the entered fact only: no identity is ever
        // declared on a row of a client carrier (Q-ROWLBL).
        Assert.Equal(
            new[] { "WaterWeightG" },
            typeof(ControloCreateEndpoints.PesoRowRequest).GetProperties(DeclaredInstance)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));

        // The edit/submit/associate carriers take their identity from the route, never from the
        // body, so the body carries no Guid at all.
        Assert.DoesNotContain(
            typeof(ControloCreateEndpoints.UpdatePesoRequest).GetProperties(DeclaredInstance),
            property => property.PropertyType == typeof(Guid) ||
                        property.PropertyType == typeof(Guid?));

        // The backend owns allocation and returns it: Created is a sealed nested result record whose
        // two members are the allocated identity and version one, and the create response transports
        // exactly that server-returned identity.
        Assert.True(typeof(PesoResult.Created).IsSealed);
        Assert.Equal(typeof(PesoResult), typeof(PesoResult.Created).BaseType);
        var created = typeof(PesoResult.Created).GetProperties(DeclaredInstance)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
        Assert.Equal(new[] { "PesoId", "Version" }, created.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(typeof(Guid), created["PesoId"]);
        Assert.Equal(typeof(int), created["Version"]);

        var response = typeof(ControloCreateEndpoints.PesoCreatedResponse).GetProperties(DeclaredInstance)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
        Assert.Equal(new[] { "PesoId", "Version" }, response.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(typeof(Guid), response["PesoId"]);
        Assert.Equal(typeof(int), response["Version"]);
    }

    /// <summary>
    /// PID9 (AC-P6) — the association candidates are real <c>cm_contexts</c> rows resolving to the
    /// anchor Tool: the Job On candidates read returns exactly the recorded CM context of the
    /// occurrences that use the Tool (occurrence order, real identities and real human facts), skips
    /// occurrences without a CM context, never leaks another Tool's context and never synthesizes,
    /// ranks or scores anything; the Controlo service surfaces the same candidates without adding
    /// anything.
    /// </summary>
    [Fact]
    public async Task PID9_AssociationCandidatesReturnOnlyRealCmContextRowsOfTheAnchorTool()
    {
        var toolA = Guid.NewGuid();
        var toolB = Guid.NewGuid();
        var unusedTool = Guid.NewGuid();

        var occurrenceJ1 = Guid.NewGuid();
        var cmContextC1 = Guid.NewGuid();
        var occurrenceJ2 = Guid.NewGuid(); // uses tool A but owns NO cm context
        var occurrenceJ3 = Guid.NewGuid();
        var cmContextC2 = Guid.NewGuid();

        var occurrences = new Dictionary<Guid, DomainJobOn>
        {
            [occurrenceJ1] = new DomainJobOn(
                JobOnId.From(occurrenceJ1),
                "REF-A",
                "100",
                MachineCode.From("B1"),
                null,
                null,
                Version: 1,
                [
                    new ToolContext(
                        ToolContextType.Cm,
                        cmContextC1,
                        JobOnId.From(occurrenceJ1),
                        ToolId.From(toolA),
                        new ToolContextSnapshot(ToolType.Cm, "5447T173", "12")),
                ]),
            [occurrenceJ2] = new DomainJobOn(
                JobOnId.From(occurrenceJ2),
                "REF-A",
                "101",
                MachineCode.From("B2"),
                null,
                null,
                Version: 1,
                []),
            [occurrenceJ3] = new DomainJobOn(
                JobOnId.From(occurrenceJ3),
                "REF-B",
                "200",
                MachineCode.From("C1"),
                null,
                null,
                Version: 1,
                [
                    new ToolContext(
                        ToolContextType.Cm,
                        cmContextC2,
                        JobOnId.From(occurrenceJ3),
                        ToolId.From(toolB),
                        new ToolContextSnapshot(ToolType.Cm, "CM-B", "LOT-B")),
                ]),
        };

        var jobOns = new FakeJobOnRepository(occurrences);
        var tools = new FakeToolRepository();
        tools.AddUsages(
            toolA,
            new ToolUsageOccurrence(occurrenceJ1, "REF-A", "100", "B1"),
            new ToolUsageOccurrence(occurrenceJ2, "REF-A", "101", "B2"));
        tools.AddUsages(
            toolB,
            new ToolUsageOccurrence(occurrenceJ3, "REF-B", "200", "C1"));

        var service = new JobOnService(jobOns, tools, Array.Empty<IJobOnDependencyProbe>());
        var cancellationToken = CancellationToken.None;

        // Tool A: exactly the one real cm_contexts row (J1's); J2 contributed no candidate even
        // though it uses the Tool, and tool B's context never leaks in.
        var candidatesForA = Assert.IsType<JobOnResult.AssociationCandidates>(
            await service.ListPesoAssociationCandidatesAsync(toolA, cancellationToken)).Candidates;
        var candidateA = Assert.Single(candidatesForA);
        Assert.Equal(cmContextC1, candidateA.CmContextId);
        Assert.Equal(occurrenceJ1, candidateA.JobOnId);
        Assert.Equal("REF-A", candidateA.Reference);
        Assert.Equal("100", candidateA.ProductionNumber);
        Assert.Equal("B1", candidateA.Machine);

        // Tool B: its own real context (J3's), with the occurrence's real facts.
        var candidatesForB = Assert.IsType<JobOnResult.AssociationCandidates>(
            await service.ListPesoAssociationCandidatesAsync(toolB, cancellationToken)).Candidates;
        var candidateB = Assert.Single(candidatesForB);
        Assert.Equal(cmContextC2, candidateB.CmContextId);
        Assert.Equal(occurrenceJ3, candidateB.JobOnId);
        Assert.Equal("REF-B", candidateB.Reference);
        Assert.Equal("200", candidateB.ProductionNumber);
        Assert.Equal("C1", candidateB.Machine);

        // A Tool with no occurrences yields an explicit empty candidate set: no synthesized row.
        Assert.Empty(Assert.IsType<JobOnResult.AssociationCandidates>(
            await service.ListPesoAssociationCandidatesAsync(unusedTool, cancellationToken)).Candidates);

        // The candidate carrier carries the five real facts and nothing else: no rank, score,
        // recency, default-selection or synthesized-context member exists.
        Assert.Equal(
            new[] { "CmContextId", "JobOnId", "Machine", "ProductionNumber", "Reference" },
            typeof(PesoAssociationCandidate).GetProperties(DeclaredInstance)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));

        // The Controlo service surfaces the SAME real candidates through the composed Job On read:
        // the delegation adds nothing and filters nothing.
        var controloService = new ControloCreateService(
            new FakePesoRepository(),
            new FakePesoContextRead(),
            service,
            new FakeToolService(),
            new EmptyCalculationConfiguration(),
            new FakeGlassDensitySettingsRepository());
        var pesoCandidates = Assert.IsType<PesoResult.Candidates>(
            await controloService.ListAssociationCandidatesAsync(toolA, cancellationToken)).Value;
        var pesoCandidate = Assert.Single(pesoCandidates);
        Assert.Equal(cmContextC1, pesoCandidate.CmContextId);
        Assert.Equal(occurrenceJ1, pesoCandidate.JobOnId);
    }

    /// <summary>A Job On repository seeded with the exact occurrences the scenario needs.</summary>
    private sealed class FakeJobOnRepository : IJobOnRepository
    {
        private readonly IReadOnlyDictionary<Guid, DomainJobOn> _occurrences;

        public FakeJobOnRepository(IReadOnlyDictionary<Guid, DomainJobOn> occurrences)
        {
            _occurrences = occurrences;
        }

        public Task<DomainJobOn?> GetByIdAsync(Guid jobOnId, CancellationToken cancellationToken) =>
            Task.FromResult(_occurrences.TryGetValue(jobOnId, out var occurrence) ? occurrence : null);

        public Task<DomainJobOn?> FindByProductionAsync(
            string reference,
            string productionNumber,
            CancellationToken cancellationToken) =>
            Task.FromResult<DomainJobOn?>(null);

        public Task<IReadOnlyList<JobOnProductionListItem>> ListByReferenceAsync(
            string reference,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobOnProductionListItem>>([]);

        public Task<DomainJobOn> CreatedAsync(
            DomainJobOn jobOn,
            IReadOnlyList<ToolContext> contexts,
            CancellationToken cancellationToken) =>
            Task.FromResult(jobOn);

        public Task<DomainJobOn> UpdatedAsync(
            DomainJobOn jobOn,
            IReadOnlyList<ToolContextChange> changes,
            CancellationToken cancellationToken) =>
            Task.FromResult(jobOn);

        public Task<DomainJobOn> DuplicatedAsync(
            DomainJobOn duplicate,
            IReadOnlyList<ToolContext> duplicatedContexts,
            int expectedSourceVersion,
            CancellationToken cancellationToken) =>
            Task.FromResult(duplicate);

        public Task DeletedAsync(Guid jobOnId, int expectedVersion, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<JobOnDependency>> ListLineageDependentsAsync(
            Guid jobOnId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobOnDependency>>([]);
    }

    /// <summary>A Tool repository recording the usage occurrences per Tool.</summary>
    private sealed class FakeToolRepository : IToolRepository
    {
        private readonly Dictionary<Guid, List<ToolUsageOccurrence>> _usages = new();

        public void AddUsages(Guid toolId, params ToolUsageOccurrence[] usages)
        {
            _usages[toolId] = usages.ToList();
        }

        public Task<Tool?> GetByIdAsync(Guid toolId, CancellationToken cancellationToken) =>
            Task.FromResult<Tool?>(null);

        public Task<IReadOnlyList<Tool>> SearchAsync(
            ToolSearchCriteria criteria,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Tool>>([]);

        public Task<Tool?> FindByIdentityAsync(
            ToolType type,
            string reference,
            string lot,
            CancellationToken cancellationToken) =>
            Task.FromResult<Tool?>(null);

        public Task<Tool> CreatedAsync(
            Tool tool,
            IReadOnlyList<MachineCode> compatibleMachines,
            CancellationToken cancellationToken) =>
            Task.FromResult(tool);

        public Task<IReadOnlyList<ToolUsageOccurrence>> ListUsageOccurrencesAsync(
            Guid toolId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ToolUsageOccurrence>>(
                _usages.TryGetValue(toolId, out var usages) ? usages : []);
    }

    /// <summary>A Peso repository that stores nothing (the candidates read never writes).</summary>
    private sealed class FakePesoRepository : IPesoRepository
    {
        public Task<Peso?> GetByIdAsync(Guid pesoId, CancellationToken cancellationToken) =>
            Task.FromResult<Peso?>(null);

        public Task<Peso> CreatedAsync(
            Peso peso,
            IReadOnlyList<PesoMeasurementRow> rows,
            CancellationToken cancellationToken) =>
            Task.FromResult(peso);

        public Task<Peso> UpdatedAsync(
            Peso peso,
            IReadOnlyList<PesoMeasurementRow> rows,
            CancellationToken cancellationToken) =>
            Task.FromResult(peso);

        public Task<Peso> SubmittedAsync(Peso peso, CancellationToken cancellationToken) =>
            Task.FromResult(peso);

        public Task<Peso> AssociatedAsync(Peso peso, CancellationToken cancellationToken) =>
            Task.FromResult(peso);
    }

    /// <summary>A context read that resolves nothing (not used by the candidates read).</summary>
    private sealed class FakePesoContextRead : IPesoContextRead
    {
        public Task<CmContextProjection?> GetCmContextAsync(Guid cmId, CancellationToken cancellationToken) =>
            Task.FromResult<CmContextProjection?>(null);
    }

    /// <summary>A Tool service whose reads all miss (not used by the candidates read).</summary>
    private sealed class FakeToolService : IToolService
    {
        public Task<ToolResult> SearchAsync(ToolSearchQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<ToolResult>(new ToolResult.SearchResults([]));

        public Task<ToolResult> GetAsync(Guid toolId, CancellationToken cancellationToken) =>
            Task.FromResult<ToolResult>(new ToolResult.NotFound(toolId));

        public Task<ToolResult> CreateAsync(CreateToolCommand command, CancellationToken cancellationToken) =>
            Task.FromResult<ToolResult>(new ToolResult.Created(Guid.NewGuid()));
    }

    /// <summary>A WATER-only calculation configuration that resolves nothing (never consulted here).</summary>
    private sealed class EmptyCalculationConfiguration : IControloCalculationConfiguration
    {
        public bool TryGetWaterDensity(decimal waterTemperature, out decimal waterDensity)
        {
            waterDensity = default;
            return false;
        }
    }
}