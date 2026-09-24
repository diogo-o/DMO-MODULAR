using DMO.Application.ControloCreate;
using DMO.Application.Documents;
using DMO.Application.JobOn;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Tools;
using DomainJobOn = DMO.Domain.JobOn.JobOn;
using DomainJobOnId = DMO.Domain.JobOn.JobOnId;

namespace DMO.IntegrationTests.JobOn;

/// <summary>
/// Test-owned in-memory implementation of the P2-T04 repository contracts and the two
/// outputs-slice seams (<see cref="IPesoOutputRead"/> + <see cref="IPesoPdfDocumentRead"/>), with
/// the contracted persistence semantics.
/// </summary>
/// <remarks>
/// <para>
/// The P2-T04 HTTP-class tests exercise the real <see cref="ToolService"/> and
/// <c>JobOnService</c> over this store, so they prove the transport, the policies and the
/// orchestration without requiring a disposable PostgreSQL database. The schema itself is proven
/// separately by the env-gated DB-class tests. The outputs-slice tests run the real
/// <c>JobOnControlOutputsService</c>, the sheet page and the open route over this same store:
/// seeded Pesos and seeded PDF bytes drive the rendered section and the open response.
/// </para>
/// <para>
/// Every write is built in locals and committed only at the end, so a forced failure leaves NOTHING
/// behind — the same unit-of-work the real repository implements with a database transaction. That is
/// what makes the "forced failure ⇒ total rollback" rows meaningful.
/// </para>
/// </remarks>
internal sealed class P2T04TestStore : IToolRepository, IJobOnRepository, IPesoOutputRead, IPesoPdfDocumentRead
{
    private readonly Dictionary<Guid, Tool> _tools = [];
    private readonly Dictionary<Guid, IReadOnlyList<ToolContext>> _contexts = [];
    private readonly Dictionary<Guid, DomainJobOn> _jobOns = [];

    /// <summary>The related-Peso anchors of the outputs slice: (peso_id, cm_id) in seed order.</summary>
    private readonly List<(Guid PesoId, Guid CmId)> _pesoAnchors = [];

    /// <summary>The seeded PDF contents of the outputs slice: peso_id → (file name, bytes).</summary>
    private readonly Dictionary<Guid, (string FileName, byte[] Content)> _pesoPdfs = [];

    /// <summary>When set, a canonical Tool create fails before anything is written.</summary>
    public bool FailToolCreate { get; set; }

    /// <summary>When set, a Job On create fails after the occurrence row would have been written.</summary>
    public bool FailJobOnCreate { get; set; }

    /// <summary>When set, a Job On edit fails after the fact changes would have been applied.</summary>
    public bool FailJobOnUpdate { get; set; }

    /// <summary>When set, a duplication fails mid-transaction, after the occurrence row.</summary>
    public bool FailJobOnDuplicate { get; set; }

    /// <summary>When set, a delete fails between the context delete and the row delete.</summary>
    public bool FailJobOnDeleteAfterContexts { get; set; }

    /// <summary>The number of stored occurrences.</summary>
    public int JobOnCount => _jobOns.Count;

    /// <summary>The number of stored canonical Tools.</summary>
    public int ToolCount => _tools.Count;

    /// <summary>The number of stored context rows of every kind.</summary>
    public int ContextCount => _contexts.Values.Sum(contexts => contexts.Count);

    /// <summary>Seeds a canonical Tool directly (test arrangement only).</summary>
    public Tool SeedTool(
        ToolType type,
        string reference,
        string lot,
        Processo? processo = null,
        int? quantity = null,
        params string[] machines)
    {
        var codes = machines.Length == 0 ? [MachineCode.From("B1")] : machines.Select(MachineCode.From).ToList();
        var tool = new Tool(ToolId.New(), type, reference, lot, processo, quantity, codes);
        _tools[tool.ToolId.Value] = tool;

        return tool;
    }

    /// <summary>Seeds a production occurrence directly (test arrangement only).</summary>
    public DomainJobOn SeedJobOn(
        string reference,
        string productionNumber,
        string machine = "B1",
        DateOnly? productionDate = null,
        Guid? copiedFromJobOnId = null,
        IReadOnlyList<ToolContext>? contexts = null)
    {
        var jobOnId = DomainJobOnId.New();
        var jobOn = new DomainJobOn(
            jobOnId,
            reference,
            productionNumber,
            MachineCode.From(machine),
            productionDate,
            copiedFromJobOnId,
            1,
            contexts ?? []);

        _jobOns[jobOnId.Value] = jobOn;
        _contexts[jobOnId.Value] = jobOn.Contexts;

        return jobOn;
    }

    /// <summary>Applies a version bump directly, to arrange a stale-version scenario.</summary>
    public void BumpVersion(Guid jobOnId)
    {
        var jobOn = _jobOns[jobOnId];
        _jobOns[jobOnId] = jobOn with { Version = jobOn.Version + 1 };
    }

    /// <summary>Changes a canonical Tool's own metadata (the live-row change of the snapshot rows).</summary>
    public void ChangeToolMetadata(Guid toolId, string reference, string lot)
    {
        var tool = _tools[toolId];
        _tools[toolId] = tool with { Reference = reference, Lot = lot };
    }

    /// <summary>Removes a production occurrence directly, to arrange a deleted-row scenario.</summary>
    public void RemoveJobOn(Guid jobOnId)
    {
        _jobOns.Remove(jobOnId);
        _contexts.Remove(jobOnId);
    }

    /// <summary>
    /// Disclosed P2-T07 additive test-arrangement helpers: the LIVE context rows of one production
    /// and the known production ids (the same rows/ids the real Job On service reads). The
    /// Boquilhas test store resolves contexts created through the REAL association flow
    /// (<c>IJobOnService.UpdateAsync</c>, BQ-slot Set) from this single source of truth instead of
    /// duplicating them in its mirror.
    /// </summary>
    public IReadOnlyList<ToolContext> ContextsOf(Guid jobOnId) =>
        _contexts.TryGetValue(jobOnId, out var contexts) ? contexts : [];

    /// <summary>The ids of every known production occurrence.</summary>
    public IReadOnlyList<Guid> JobOnIds() =>
        _contexts.Keys.ToList();

    /// <summary>Seeds a production-bound Peso anchored on the supplied <c>cm_id</c> (outputs-slice
    /// arrangement; the id is allocated by the arrangement).</summary>
    public Guid SeedPeso(Guid cmId)
    {
        var pesoId = Guid.NewGuid();
        _pesoAnchors.Add((pesoId, cmId));

        return pesoId;
    }

    /// <summary>Seeds the stored Peso PDF of one Peso (outputs-slice arrangement: the content the
    /// document route must stream back).</summary>
    public void SeedPesoPdf(Guid pesoId, string fileName, byte[] content) =>
        _pesoPdfs[pesoId] = (fileName, content);

    // ---- IPesoOutputRead (outputs slice: jobon_id → related peso_id values) -------------------

    Task<IReadOnlyList<Guid>> IPesoOutputRead.ListByJobOnIdAsync(
        Guid jobOnId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(RelatedPesoIds(jobOnId));

    private IReadOnlyList<Guid> RelatedPesoIds(Guid jobOnId)
    {
        // The same relation the real read resolves: a Peso is an output of the occurrence iff its
        // cm_id is a real cm_contexts row of that occurrence. The store keeps the real context
        // rows; the anchors keep seed order.
        var cmIds = _contexts.TryGetValue(jobOnId, out var contexts)
            ? contexts
                .Where(context => context.ContextType == ToolContextType.Cm)
                .Select(context => context.ContextId)
                .ToHashSet()
            : [];

        return _pesoAnchors
            .Where(anchor => cmIds.Contains(anchor.CmId))
            .Select(anchor => anchor.PesoId)
            .ToList();
    }

    // ---- IPesoPdfDocumentRead (outputs slice: availability + content of the seeded PDFs) -------

    Task<PesoPdfAvailabilityResult> IPesoPdfDocumentRead.GetAvailabilityAsync(
        Guid pesoId,
        CancellationToken cancellationToken)
    {
        PesoPdfAvailabilityResult result = _pesoPdfs.TryGetValue(pesoId, out var pdf)
            ? new PesoPdfAvailabilityResult.Available(pdf.FileName, $"Peso/{pdf.FileName}")
            : new PesoPdfAvailabilityResult.NotGenerated();

        return Task.FromResult(result);
    }

    Task<PesoPdfContentResult> IPesoPdfDocumentRead.ReadAsync(
        Guid pesoId,
        CancellationToken cancellationToken)
    {
        PesoPdfContentResult result = _pesoPdfs.TryGetValue(pesoId, out var pdf)
            ? new PesoPdfContentResult.Found(pdf.FileName, $"Peso/{pdf.FileName}", pdf.Content)
            : new PesoPdfContentResult.NotGenerated();

        return Task.FromResult(result);
    }

    // ---- IToolRepository ------------------------------------------------------------------

    public Task<Tool?> GetByIdAsync(Guid toolId, CancellationToken cancellationToken) =>
        Task.FromResult(_tools.TryGetValue(toolId, out var tool) ? tool : null);

    public Task<IReadOnlyList<Tool>> SearchAsync(
        ToolSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        IEnumerable<Tool> query = _tools.Values;

        if (criteria.Type is { } type)
        {
            query = query.Where(tool => tool.Type == type);
        }

        if (criteria.Reference is { } reference)
        {
            query = query.Where(tool => string.Equals(tool.Reference, reference, StringComparison.Ordinal));
        }

        if (criteria.Lot is { } lot)
        {
            query = query.Where(tool => string.Equals(tool.Lot, lot, StringComparison.Ordinal));
        }

        if (criteria.Query is { } text)
        {
            query = query.Where(tool =>
                tool.Reference.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                tool.Lot.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        if (criteria.Machine is { } machine)
        {
            query = query.Where(tool =>
                tool.CompatibleMachines.Any(candidate => candidate.Value == machine.Value));
        }

        IReadOnlyList<Tool> result = query
            .OrderBy(tool => ToolTokens.ToToken(tool.Type), StringComparer.Ordinal)
            .ThenBy(tool => tool.Reference, StringComparer.Ordinal)
            .ThenBy(tool => tool.Lot, StringComparer.Ordinal)
            .ThenBy(tool => tool.ToolId.Value)
            .Take(criteria.Limit)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<Tool?> FindByIdentityAsync(
        ToolType type,
        string reference,
        string lot,
        CancellationToken cancellationToken) =>
        Task.FromResult(_tools.Values.FirstOrDefault(tool =>
            tool.Type == type &&
            string.Equals(tool.Reference, reference, StringComparison.Ordinal) &&
            string.Equals(tool.Lot, lot, StringComparison.Ordinal)));

    public Task<Tool> CreatedAsync(
        Tool tool,
        IReadOnlyList<MachineCode> compatibleMachines,
        CancellationToken cancellationToken)
    {
        if (_tools.Values.Any(existing =>
                existing.Type == tool.Type &&
                string.Equals(existing.Reference, tool.Reference, StringComparison.Ordinal) &&
                string.Equals(existing.Lot, tool.Lot, StringComparison.Ordinal)))
        {
            throw new ToolPersistenceException(
                ToolPersistenceFailureReason.DuplicateIdentity,
                "A canonical Tool with this identity tuple already exists.",
                existingToolId: _tools.Values.First(candidate =>
                    candidate.Type == tool.Type &&
                    string.Equals(candidate.Reference, tool.Reference, StringComparison.Ordinal) &&
                    string.Equals(candidate.Lot, tool.Lot, StringComparison.Ordinal)).ToolId.Value);
        }

        if (FailToolCreate)
        {
            throw new InvalidOperationException("Forced canonical Tool create failure (test double).");
        }

        // Tool row + its full compatibility set are one unit.
        _tools[tool.ToolId.Value] = tool with { CompatibleMachines = compatibleMachines };

        return Task.FromResult(_tools[tool.ToolId.Value]);
    }

    public Task<IReadOnlyList<ToolUsageOccurrence>> ListUsageOccurrencesAsync(
        Guid toolId,
        CancellationToken cancellationToken)
    {
        var usages = _contexts
            .Where(entry => entry.Value.Any(context => context.ToolId.Value == toolId))
            .Select(entry => _jobOns[entry.Key])
            .OrderBy(jobOn => jobOn.Reference, StringComparer.Ordinal)
            .ThenBy(jobOn => jobOn.ProductionNumber, StringComparer.Ordinal)
            .ThenBy(jobOn => jobOn.JobOnId.Value)
            .Select(jobOn => new ToolUsageOccurrence(
                jobOn.JobOnId.Value, jobOn.Reference, jobOn.ProductionNumber, jobOn.Machine.Value))
            .ToList();

        return Task.FromResult<IReadOnlyList<ToolUsageOccurrence>>(usages);
    }

    // ---- IJobOnRepository -----------------------------------------------------------------

    Task<DomainJobOn?> IJobOnRepository.GetByIdAsync(Guid jobOnId, CancellationToken cancellationToken) =>
        Task.FromResult(_jobOns.TryGetValue(jobOnId, out var jobOn)
            ? jobOn with { Contexts = _contexts[jobOnId] }
            : null);

    public Task<DomainJobOn?> FindByProductionAsync(
        string reference,
        string productionNumber,
        CancellationToken cancellationToken)
    {
        var match = _jobOns.Values.FirstOrDefault(jobOn =>
            string.Equals(jobOn.Reference, reference, StringComparison.Ordinal) &&
            string.Equals(jobOn.ProductionNumber, productionNumber, StringComparison.Ordinal));

        return Task.FromResult(match is null ? null : match with { Contexts = _contexts[match.JobOnId.Value] });
    }

    public Task<IReadOnlyList<JobOnProductionListItem>> ListByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        // production_number ASC, production_date ASC NULLS LAST, jobon_id ASC.
        IReadOnlyList<JobOnProductionListItem> items = _jobOns.Values
            .Where(jobOn => string.Equals(jobOn.Reference, reference, StringComparison.Ordinal))
            .OrderBy(jobOn => jobOn.ProductionNumber, StringComparer.Ordinal)
            .ThenBy(jobOn => jobOn.ProductionDate ?? DateOnly.MaxValue)
            .ThenBy(jobOn => jobOn.JobOnId.Value)
            .Select(jobOn => new JobOnProductionListItem(
                jobOn.JobOnId.Value,
                jobOn.Reference,
                jobOn.ProductionNumber,
                jobOn.Machine.Value,
                jobOn.ProductionDate))
            .ToList();

        return Task.FromResult(items);
    }

    public Task<DomainJobOn> CreatedAsync(
        DomainJobOn jobOn,
        IReadOnlyList<ToolContext> contexts,
        CancellationToken cancellationToken)
    {
        var conflict = FindConflict(jobOn.Reference, jobOn.ProductionNumber);
        if (conflict is { } existing)
        {
            throw new JobOnPersistenceException(
                JobOnPersistenceFailureReason.DuplicateProduction,
                "Another Job On already owns this reference and production number.",
                existingJobOnId: existing);
        }

        var frozen = new List<ToolContext>(contexts.Count);
        foreach (var context in contexts)
        {
            frozen.Add(FreezeFromLiveTool(context));
        }

        if (FailJobOnCreate)
        {
            throw new InvalidOperationException("Forced Job On create failure (test double).");
        }

        var stored = jobOn with { Contexts = frozen };
        _jobOns[stored.JobOnId.Value] = stored;
        _contexts[stored.JobOnId.Value] = frozen;

        return Task.FromResult(stored);
    }

    public Task<DomainJobOn> UpdatedAsync(
        DomainJobOn jobOn,
        IReadOnlyList<ToolContextChange> changes,
        CancellationToken cancellationToken)
    {
        if (!_jobOns.TryGetValue(jobOn.JobOnId.Value, out var persisted))
        {
            throw new ConcurrencyConflictException("The Job On no longer exists.");
        }

        if (persisted.Version != jobOn.Version)
        {
            throw new ConcurrencyConflictException("The Job On was modified concurrently.");
        }

        var conflict = FindConflict(jobOn.Reference, jobOn.ProductionNumber);
        if (conflict is { } other && other != jobOn.JobOnId.Value)
        {
            throw new JobOnPersistenceException(
                JobOnPersistenceFailureReason.DuplicateProduction,
                "Another Job On already owns this reference and production number.",
                existingJobOnId: other);
        }

        // Build the whole new context set in locals, then commit once.
        var contexts = _contexts[jobOn.JobOnId.Value].ToList();

        foreach (var change in changes)
        {
            switch (change.Action)
            {
                case ToolAssociationAction.Keep:
                    break;

                case ToolAssociationAction.Set:
                    var resolved = Resolve(change.ContextType, change.ToolId!.Value);
                    var index = contexts.FindIndex(context => context.ContextType == change.ContextType);

                    if (index < 0)
                    {
                        contexts.Add(new ToolContext(
                            change.ContextType,
                            Guid.NewGuid(),
                            jobOn.JobOnId,
                            ToolId.From(change.ToolId.Value),
                            resolved));
                    }
                    else
                    {
                        // In-place update on the SAME context row.
                        contexts[index] = contexts[index] with
                        {
                            ToolId = ToolId.From(change.ToolId.Value),
                            Frozen = resolved,
                        };
                    }

                    break;

                case ToolAssociationAction.Remove:
                    contexts.RemoveAll(context => context.ContextType == change.ContextType);
                    break;
            }
        }

        if (FailJobOnUpdate)
        {
            throw new InvalidOperationException("Forced Job On edit failure (test double).");
        }

        var stored = jobOn with
        {
            Version = persisted.Version + 1,
            Contexts = contexts,
        };

        _jobOns[stored.JobOnId.Value] = stored;
        _contexts[stored.JobOnId.Value] = contexts;

        return Task.FromResult(stored);
    }

    public Task<DomainJobOn> DuplicatedAsync(
        DomainJobOn duplicate,
        IReadOnlyList<ToolContext> duplicatedContexts,
        int expectedSourceVersion,
        CancellationToken cancellationToken)
    {
        var sourceJobOnId = duplicate.CopiedFromJobOnId!.Value;

        if (!_jobOns.TryGetValue(sourceJobOnId, out var source))
        {
            throw new ConcurrencyConflictException("The duplication source no longer exists.");
        }

        if (source.Version != expectedSourceVersion)
        {
            throw new ConcurrencyConflictException("The duplication source changed after it was previewed.");
        }

        var conflict = FindConflict(duplicate.Reference, duplicate.ProductionNumber);
        if (conflict is { } existing)
        {
            throw new JobOnPersistenceException(
                JobOnPersistenceFailureReason.DuplicateProduction,
                "Another Job On already owns this reference and production number.",
                existingJobOnId: existing);
        }

        if (FailJobOnDuplicate)
        {
            throw new JobOnPersistenceException(
                JobOnPersistenceFailureReason.DependencyExists,
                "Forced mid-transaction duplication failure (test double).");
        }

        // Test double: persist exactly the duplicated contexts the real JobOnService supplied
        // (each built from the CURRENT canonical Tool row at duplication time — Owner
        // clarification, contract §23). The double itself never snapshots or clones.
        var copied = duplicatedContexts.ToList();
        var stored = duplicate with { Contexts = copied };

        _jobOns[stored.JobOnId.Value] = stored;
        _contexts[stored.JobOnId.Value] = copied;

        return Task.FromResult(stored);
    }

    public Task DeletedAsync(Guid jobOnId, int expectedVersion, CancellationToken cancellationToken)
    {
        if (!_jobOns.TryGetValue(jobOnId, out var persisted))
        {
            throw new ConcurrencyConflictException("The Job On no longer exists.");
        }

        if (persisted.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException("The Job On was modified concurrently.");
        }

        // Contexts first, then the row — one unit.
        var contexts = _contexts[jobOnId];

        if (FailJobOnDeleteAfterContexts)
        {
            throw new InvalidOperationException("Forced delete failure between contexts and row (test double).");
        }

        _contexts.Remove(jobOnId);
        _jobOns.Remove(jobOnId);
        _ = contexts;

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<JobOnDependency>> ListLineageDependentsAsync(
        Guid jobOnId,
        CancellationToken cancellationToken) =>
        Task.FromResult(LineageDependencies(jobOnId));

    /// <summary>
    /// The recorded duplication lineage of an occurrence, with the same kind and wording the real
    /// <c>JobOnLineageDependencyProbe</c> reports. A Job On used as a duplication source is a
    /// dependency: its lineage is persisted history and is never silently removed.
    /// </summary>
    public IReadOnlyList<JobOnDependency> LineageDependencies(Guid jobOnId)
    {
        IReadOnlyList<JobOnDependency> dependents = _jobOns.Values
            .Where(jobOn => jobOn.CopiedFromJobOnId == jobOnId)
            .Select(jobOn => new JobOnDependency(
                "duplication-lineage",
                $"O Job On '{jobOn.Reference}' / produção '{jobOn.ProductionNumber}' foi duplicado a partir deste Job On."))
            .ToList();

        return dependents;
    }

    private Guid? FindConflict(string reference, string productionNumber)
    {
        var match = _jobOns.Values.FirstOrDefault(jobOn =>
            string.Equals(jobOn.Reference, reference, StringComparison.Ordinal) &&
            string.Equals(jobOn.ProductionNumber, productionNumber, StringComparison.Ordinal));

        return match?.JobOnId.Value;
    }

    private ToolContext FreezeFromLiveTool(ToolContext context)
    {
        var tool = _tools.TryGetValue(context.ToolId.Value, out var found)
            ? found
            : throw new JobOnPersistenceException(
                JobOnPersistenceFailureReason.ToolNotFound,
                "The supplied context Tool does not exist.");

        var required = ToolTokens.RequiredToolType(context.ContextType);
        if (tool.Type != required)
        {
            throw new JobOnPersistenceException(
                JobOnPersistenceFailureReason.ToolTypeMismatch,
                "The supplied context Tool's type does not match its slot.");
        }

        return context with { Frozen = new ToolContextSnapshot(tool.Type, tool.Reference, tool.Lot) };
    }

    private ToolContextSnapshot Resolve(ToolContextType contextType, Guid toolId)
    {
        var tool = _tools.TryGetValue(toolId, out var found)
            ? found
            : throw new JobOnPersistenceException(
                JobOnPersistenceFailureReason.ToolNotFound,
                "The supplied context Tool does not exist.");

        var required = ToolTokens.RequiredToolType(contextType);
        if (tool.Type != required)
        {
            throw new JobOnPersistenceException(
                JobOnPersistenceFailureReason.ToolTypeMismatch,
                "The supplied context Tool's type does not match its slot.");
        }

        return new ToolContextSnapshot(tool.Type, tool.Reference, tool.Lot);
    }
}
