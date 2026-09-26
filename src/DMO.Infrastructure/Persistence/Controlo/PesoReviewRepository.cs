using DMO.Application.ControloApprove;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;
using DMO.Infrastructure.Persistence.Entities;
using DMO.Infrastructure.Persistence.EntityConfigurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using DMO.Infrastructure.Persistence;
namespace DMO.Infrastructure.Persistence.Controlo;

/// <summary>
/// <see cref="IPesoReviewRepository"/> implementation over the single application persistence
/// context: the two backend-filtered list queries (pending + local Histórico), the per-Peso
/// decision trail and the atomic decision write.
/// </summary>
/// <remarks>
/// <para>
/// The list rows compose the reviewable facts in SQL: the fixed reviewable predicate
/// (<c>submitted_at IS NOT NULL AND status = 'pendente'</c>), the visibility predicate of the
/// local Histórico (<c>submitted OR has a decision event</c>), every filter as a backend SQL
/// predicate over backend-reported facts (traversal through <c>cm_id â†’ cm_contexts â†’ job_ons</c>
/// plus the frozen/pending Tool facts â€” never reconstructed from display text), deterministic
/// ordering and one page. No client-side filtering of a fetched set exists (PL2/AC-AP1/AC-H2).</para>
/// <para>
/// <c>DecisionAsync</c> applies one decision event atomically â€” the status transition on the
/// existing <c>pesos</c> row (version-guarded) + the append-only decision row in ONE transaction
/// (Â§10): approve â†’ <c>'aprovado'</c>, reject â†’ <c>'nao_aprovado'</c>, reopen â†’ <c>'pendente'</c>
/// with the submitted handoff cleared (so the CLOSED P2-T05 edit/submit routes work again);
/// <c>pesos.version</c> increments exactly once. The reason CHECK backstop maps to the same
/// validator token, never a 500 (Â§8.2). Decision rows are immutable after COMMIT: no update path
/// and no delete path exists (AC-MG3).</para>
/// </remarks>
public sealed class PesoReviewRepository : IPesoReviewRepository
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the repository over the application persistence context.</summary>
    public PesoReviewRepository(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PesoReviewRow>> GetPendingAsync(
        PendingListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The reviewable predicate (pinned, P2-T05 §3.1): submitted AND still pendente. The
        // traversal filters are translatable EXISTS predicates; the traversal FACTS of the page
        // are resolved in batched reads below.
        var filtered = Pesos.AsNoTracking().Where(peso =>
            peso.SubmittedAt != null
            && peso.Status == PesoStatusTokens.ToToken(PesoStatus.Pendente));

        filtered = ApplyTraversalFilters(filtered, query.Reference, query.ProductionNumber, query.Machine, query.Processo, query.ToolReference);

        if (query.SubmittedFrom is { } from)
        {
            filtered = filtered.Where(peso => peso.SubmittedAt >= from);
        }

        if (query.SubmittedTo is { } to)
        {
            filtered = filtered.Where(peso => peso.SubmittedAt <= to);
        }

        var page = await filtered
            .OrderBy(peso => peso.SubmittedAt)
            .ThenBy(peso => peso.PesoId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return await ComposeRowsAsync(page, withTrail: false, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PesoReviewRow>> GetHistoryAsync(
        HistoryListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Visibility predicate (pinned, §15.2): submitted OR has at least one decision event — a
        // reopened draft keeps its trail visible. Drafts never appear (they never entered the
        // review circuit).
        var filtered = Pesos.AsNoTracking().Where(peso =>
            peso.SubmittedAt != null
            || Decisions.Any(decision => decision.PesoId == peso.PesoId));

        if (query.ReviewState is { } reviewState)
        {
            // The review-state FILTER vocabulary (not a status): 'pendente'/'submetido' both select
            // the reviewable subset; 'aprovado'/'nao_aprovado' select by the current status.
            if (string.Equals(reviewState, "pendente", StringComparison.Ordinal)
                || string.Equals(reviewState, "submetido", StringComparison.Ordinal))
            {
                filtered = filtered.Where(peso =>
                    peso.SubmittedAt != null
                    && peso.Status == PesoStatusTokens.ToToken(PesoStatus.Pendente));
            }
            else
            {
                filtered = filtered.Where(peso => peso.Status == reviewState);
            }
        }

        filtered = ApplyTraversalFilters(filtered, query.Reference, query.ProductionNumber, query.Machine, query.Processo, query.ToolReference);

        if (query.SubmittedFrom is { } submittedFrom)
        {
            filtered = filtered.Where(peso => peso.SubmittedAt >= submittedFrom);
        }

        if (query.SubmittedTo is { } submittedTo)
        {
            filtered = filtered.Where(peso => peso.SubmittedAt <= submittedTo);
        }

        var decidedFrom = query.DecidedFrom;
        var decidedTo = query.DecidedTo;
        var decidedBy = query.DecidedByUserId;
        var decisionToken = query.Decision;

        if (decidedFrom is not null
            || decidedTo is not null
            || decidedBy is not null
            || decisionToken is not null)
        {
            filtered = filtered.Where(peso => Decisions.Any(decision =>
                decision.PesoId == peso.PesoId
                && (decidedFrom == null || decision.DecidedAt >= decidedFrom)
                && (decidedTo == null || decision.DecidedAt <= decidedTo)
                && (decidedBy == null || decision.DecidedByUserId == decidedBy)
                && (decisionToken == null || decision.Decision == decisionToken)));
        }

        var page = await filtered
            .OrderByDescending(peso => peso.SubmittedAt)
            .ThenByDescending(peso => peso.PesoId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return await ComposeRowsAsync(page, withTrail: true, cancellationToken);
    }

    /// <summary>
    /// Composes the backend-reported list rows of one page: the measurement-row counts, the
    /// decision-trail facts (when requested) and the traversal production facts are resolved in
    /// batched reads over the page's ids — every fact stays a backend-reported SQL fact and the
    /// composed reads avoid non-translatable nested correlated subqueries.
    /// </summary>
    private async Task<IReadOnlyList<PesoReviewRow>> ComposeRowsAsync(
        IReadOnlyList<PesoEntity> page,
        bool withTrail,
        CancellationToken cancellationToken)
    {
        var pageIds = page.Select(peso => peso.PesoId).ToList();

        var rowCounts = pageIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await Rows.AsNoTracking()
                .Where(row => pageIds.Contains(row.PesoId))
                .GroupBy(row => row.PesoId)
                .Select(group => new { PesoId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(entry => entry.PesoId, entry => entry.Count, cancellationToken);

        var trailRows = pageIds.Count == 0
            ? new List<PesoReviewDecisionEntity>()
            : await Decisions.AsNoTracking()
                .Where(decision => pageIds.Contains(decision.PesoId))
                .OrderBy(decision => decision.DecidedAt)
                .ToListAsync(cancellationToken);

        var trailByPeso = trailRows
            .GroupBy(decision => decision.PesoId)
            .ToDictionary(group => group.Key, group => group.ToList());

        // Traversal facts: cm_contexts (production anchor) + job_ons (production labels) + tools
        // (frozen/pending references and processo) — batched over the page's anchors.
        var cmIds = page
            .Where(peso => peso.CmId is not null)
            .Select(peso => peso.CmId!.Value)
            .Distinct()
            .ToList();

        var cmBy = cmIds.Count == 0
            ? new Dictionary<Guid, CmContextEntity>()
            : await CmContexts.AsNoTracking()
                .Where(context => cmIds.Contains(context.CmId))
                .ToDictionaryAsync(context => context.CmId, cancellationToken);

        var jobOnIds = cmBy.Values.Select(context => context.JobOnId).Distinct().ToList();
        var jobOnBy = jobOnIds.Count == 0
            ? new Dictionary<Guid, JobOnEntity>()
            : await JobOns.AsNoTracking()
                .Where(occurrence => jobOnIds.Contains(occurrence.JobOnId))
                .ToDictionaryAsync(occurrence => occurrence.JobOnId, cancellationToken);

        var toolIds = cmBy.Values
            .Select(context => context.ToolId)
            .Concat(page.Where(peso => peso.ToolId is not null).Select(peso => peso.ToolId!.Value))
            .Distinct()
            .ToList();

        var tools = toolIds.Count == 0
            ? new List<ToolEntity>()
            : await Tools.AsNoTracking()
                .Where(tool => toolIds.Contains(tool.ToolId))
                .ToListAsync(cancellationToken);

        return page
            .Select(peso =>
            {
                var cm = peso.CmId is { } cmId ? cmBy.GetValueOrDefault(cmId) : null;
                var jobOn = cm is not null ? jobOnBy.GetValueOrDefault(cm.JobOnId) : null;
                var tool = cm is not null ? ToolById(tools, cm.ToolId) : null;
                var pendingTool = peso.ToolId is { } toolId ? ToolById(tools, toolId) : null;
                var events = withTrail ? (trailByPeso.GetValueOrDefault(peso.PesoId) ?? []) : [];
                var last = events.Count == 0 ? null : events[^1];

                return Project(
                    peso,
                    cm,
                    jobOn,
                    tool,
                    pendingTool,
                    rowCount: rowCounts.GetValueOrDefault(peso.PesoId),
                    hasDecisionTrail: events.Count > 0 || trailByPeso.ContainsKey(peso.PesoId),
                    lastDecision: last is null
                        ? null
                        : new DecisionSummary(
                            last.Decision,
                            last.DecidedByUserId,
                            last.DecidedAt,
                            last.Reason),
                    decisionCount: events.Count);
            })
            .ToList();
    }
    /// <inheritdoc />
    public async Task<IReadOnlyList<PesoReviewDecision>> GetDecisionsAsync(
        Guid pesoId,
        CancellationToken cancellationToken)
    {
        var entities = await Decisions
            .AsNoTracking()
            .Where(decision => decision.PesoId == pesoId)
            .OrderBy(decision => decision.DecidedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(Project).ToList();
    }

    /// <inheritdoc />
    public async Task<PesoReviewDecision> DecisionAsync(
        PesoReviewDecision decision,
        int expectedPesoVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var pesoEntity = await Pesos.FirstOrDefaultAsync(
                    peso => peso.PesoId == decision.PesoId.Value,
                    cancellationToken)
                ?? throw new ConcurrencyConflictException(
                    $"Peso '{decision.PesoId}' no longer exists (deleted concurrently); nothing was written.");

            AssertVersion(pesoEntity, decision.PesoId.Value, expectedPesoVersion);

            // The single write path of the decision core: transition (approve/reject/reopen) +
            // version bump on the SAME row, plus the append-only decision event â€” one unit (Â§10).
            ApplyTransition(pesoEntity, decision.Decision, now);

            Decisions.Add(new PesoReviewDecisionEntity
            {
                PesoReviewDecisionId = decision.PesoReviewDecisionId.Value,
                PesoId = decision.PesoId.Value,
                Decision = PesoReviewDecisionKindTokens.ToToken(decision.Decision),
                DecidedByUserId = decision.DecidedByUserId,
                DecidedAt = decision.DecidedAt,
                Reason = decision.Decision == PesoReviewDecisionKind.Aprovado ? null : decision.Reason,
                PriorStatus = PesoStatusTokens.ToToken(decision.PriorStatus),
                PesoVersionAtDecision = decision.PesoVersionAtDecision,
                CreatedAt = now,
            });

            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryMapWriteFailure(exception, out var failure))
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            _context.ChangeTracker.Clear();
            throw failure;
        }

        return decision;
    }

    // ---------------------------------------------------------------------------------------------
    // Query composition
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The traversal filters (contract §15.1/§15.2) as translatable EXISTS predicates over
    /// <c>pesos</c> alone: reference/production number/machine through <c>cm_id → job_ons</c> (a
    /// pending Peso has no production context — it never matches), and processo/tool-reference
    /// through the frozen CM triple or the pending direct Tool. Every filter is a backend SQL
    /// predicate; the traversal FACTS of the page are resolved in batched reads inside
    /// <see cref="ComposeRowsAsync"/>.
    /// </summary>
    private IQueryable<PesoEntity> ApplyTraversalFilters(
        IQueryable<PesoEntity> source,
        string? reference,
        string? productionNumber,
        string? machine,
        string? processo,
        string? toolReference)
    {
        if (!string.IsNullOrWhiteSpace(reference))
        {
            source = source.Where(peso =>
                peso.CmId != null
                && CmContexts
                    .Join(JobOns, context => context.JobOnId, occurrence => occurrence.JobOnId,
                        (context, occurrence) => new { context.CmId, occurrence.Reference })
                    .Any(entry => entry.CmId == peso.CmId && entry.Reference == reference));
        }

        if (!string.IsNullOrWhiteSpace(productionNumber))
        {
            source = source.Where(peso =>
                peso.CmId != null
                && CmContexts
                    .Join(JobOns, context => context.JobOnId, occurrence => occurrence.JobOnId,
                        (context, occurrence) => new { context.CmId, occurrence.ProductionNumber })
                    .Any(entry => entry.CmId == peso.CmId && entry.ProductionNumber == productionNumber));
        }

        if (!string.IsNullOrWhiteSpace(machine))
        {
            source = source.Where(peso =>
                peso.CmId != null
                && CmContexts
                    .Join(JobOns, context => context.JobOnId, occurrence => occurrence.JobOnId,
                        (context, occurrence) => new { context.CmId, occurrence.Machine })
                    .Any(entry => entry.CmId == peso.CmId && entry.Machine == machine));
        }

        if (!string.IsNullOrWhiteSpace(processo))
        {
            source = source.Where(peso =>
                (peso.CmId != null
                    && CmContexts
                        .Join(Tools, context => context.ToolId, tool => tool.ToolId,
                            (context, tool) => new { context.CmId, tool.Processo })
                        .Any(entry => entry.CmId == peso.CmId && entry.Processo == processo))
                || (peso.ToolId != null
                    && Tools.Any(tool => tool.ToolId == peso.ToolId && tool.Processo == processo)));
        }

        if (!string.IsNullOrWhiteSpace(toolReference))
        {
            source = source.Where(peso =>
                (peso.CmId != null
                    && CmContexts.Any(context =>
                        context.CmId == peso.CmId && context.ToolReference == toolReference))
                || (peso.ToolId != null
                    && Tools.Any(tool => tool.ToolId == peso.ToolId && tool.Reference == toolReference)));
        }

        return source;
    }
    /// <summary>Finds one Tool by id in the in-call traversal read (a transient list lookup,
    /// never a Tool store/cache — the accepted TOL17 prohibition on Tool persistence caches).</summary>
    private static ToolEntity? ToolById(IReadOnlyList<ToolEntity> tools, Guid toolId)
    {
        foreach (var tool in tools)
        {
            if (tool.ToolId == toolId)
            {
                return tool;
            }
        }

        return null;
    }

    private static PesoReviewRow Project(
        PesoEntity peso,
        CmContextEntity? cm,
        JobOnEntity? jobOn,
        ToolEntity? tool,
        ToolEntity? pendingTool,
        int rowCount,
        bool hasDecisionTrail,
        DecisionSummary? lastDecision,
        int decisionCount) =>
        new(
            peso.PesoId,
            peso.Version,
            peso.Status,
            peso.CmId,
            peso.ToolId,
            cm is null ? null : jobOn?.Reference,
            cm is null ? null : jobOn?.ProductionNumber,
            cm is null ? null : jobOn?.Machine,
            cm is not null ? cm.ToolReference : pendingTool?.Reference,
            cm is not null ? tool?.Processo : pendingTool?.Processo,
            peso.SubmittedAt,
            peso.SubmittedByUserId,
            rowCount,
            hasDecisionTrail,
            lastDecision,
            decisionCount);

    private static PesoReviewDecision Project(PesoReviewDecisionEntity entity) => new(
        DMO.Domain.Controlo.PesoReviewDecisionId.From(entity.PesoReviewDecisionId),
        PesoId.From(entity.PesoId),
        PesoReviewDecisionKindTokens.Parse(entity.Decision) ?? PesoReviewDecisionKind.Aprovado,
        entity.DecidedByUserId,
        entity.DecidedAt,
        entity.Reason,
        PesoStatusTokens.Parse(entity.PriorStatus) ?? PesoStatus.Pendente,
        entity.PesoVersionAtDecision,
        entity.CreatedAt);

    private static void ApplyTransition(PesoEntity peso, PesoReviewDecisionKind decision, DateTimeOffset now)
    {
        switch (decision)
        {
            case PesoReviewDecisionKind.Aprovado:
                peso.Status = PesoStatusTokens.ToToken(PesoStatus.Aprovado);
                break;

            case PesoReviewDecisionKind.NaoAprovado:
                peso.Status = PesoStatusTokens.ToToken(PesoStatus.NaoAprovado);
                break;

            case PesoReviewDecisionKind.Reaberto:
                // The draft-editable handoff is restored so the CLOSED P2-T05 edit/submit routes
                // work again (P2-T05 Â§7.3/Â§7.4; contract Â§19.2): status back to 'pendente' and the
                // submitted handoff cleared. No measurement fact is touched (AC-O5).
                peso.Status = PesoStatusTokens.ToToken(PesoStatus.Pendente);
                peso.SubmittedAt = null;
                peso.SubmittedByUserId = null;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown decision kind.");
        }

        peso.Version += 1;
        peso.UpdatedAt = now;
    }

    private static void AssertVersion(PesoEntity entity, Guid pesoId, int expectedVersion)
    {
        if (entity.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                $"Peso '{pesoId}' was modified concurrently (expected version {expectedVersion}, " +
                $"current version {entity.Version}); nothing was written.");
        }
    }

    /// <summary>
    /// Saves the tracked changes, mapping a save-time optimistic-concurrency conflict onto the
    /// domain typed conflict (contract Â§11.1): the explicit in-transaction version compare closes
    /// already-stale decisions, while the active <c>IsConcurrencyToken</c> guard closes the
    /// remaining race between that compare and this save (accepted <c>SaveAsync</c> pattern).
    /// </summary>
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw ConcurrencyConflictExceptionMapping.ToDomainConflict(exception);
        }
    }

    /// <summary>
    /// Maps PostgreSQL constraint violations onto the typed application failures; everything else
    /// propagates unchanged (Â§8.2 binding rules): SQLSTATE <c>23514</c> on the reason CHECK â†’ the
    /// same validator token (never a 500); a RESTRICT FK violation (vanished Peso/user) â†’ a
    /// concurrency-style refusal (nothing written).
    /// </summary>
    private static bool TryMapWriteFailure(DbUpdateException exception, out PesoReviewPersistenceException failure)
    {
        failure = null!;

        var postgresException = exception.InnerException as Npgsql.PostgresException
            ?? exception.InnerException?.InnerException as Npgsql.PostgresException;

        if (postgresException is null)
        {
            return false;
        }

        switch (postgresException.SqlState)
        {
            case "23514": // check_violation â€” the C2-style backstop of the reason rule.
                if (string.Equals(
                        postgresException.ConstraintName,
                        PesoReviewDecisionEntityConfiguration.ReasonRequiredCheckConstraintName,
                        StringComparison.Ordinal))
                {
                    failure = new PesoReviewPersistenceException(
                        PesoReviewPersistenceFailureReason.ReasonRequired,
                        "The decision requires its contracted reason; nothing was written.");
                    return true;
                }

                break;

            case "23503": // foreign_key_violation â€” every P2-T06 FK is RESTRICT.
                failure = new PesoReviewPersistenceException(
                    PesoReviewPersistenceFailureReason.DependencyExists,
                    "A referenced record (the Peso or its actor) vanished concurrently; nothing was written.");
                return true;

            case "23505": // unique_violation â€” only the PK; a duplicate id is an application bug.
                failure = new PesoReviewPersistenceException(
                    PesoReviewPersistenceFailureReason.DependencyExists,
                    "A duplicate decision identity was attempted; nothing was written.");
                return true;
        }

        return false;
    }

    private static async Task SafeRollbackAsync(
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Already completed: nothing to undo.
        }
        catch (Npgsql.PostgresException)
        {
            // The connection already reported the failed transaction state.
        }
    }

    /// <summary>
    /// The tracked Peso query surface of the decision write and the composed list queries: the
    /// accessors return <c>IQueryable</c> over the single context (the accepted
    /// <c>ToolRepository</c> convention), so no new broad <c>DbSet</c> surface is introduced;
    /// the decision table is the ONLY P2-T06 entity surfaced as a <c>DbSet</c>.
    /// </summary>
    private IQueryable<PesoEntity> Pesos => _context.Set<PesoEntity>();

    private IQueryable<PesoMeasurementRowEntity> Rows => _context.Set<PesoMeasurementRowEntity>();

    /// <summary>The append-only decision trail surface (the only new P2-T06 <c>DbSet</c>).</summary>
    private DbSet<PesoReviewDecisionEntity> Decisions => _context.Set<PesoReviewDecisionEntity>();

    private IQueryable<CmContextEntity> CmContexts => _context.Set<CmContextEntity>();

    private IQueryable<JobOnEntity> JobOns => _context.Set<JobOnEntity>();

    private IQueryable<ToolEntity> Tools => _context.Set<ToolEntity>();
}