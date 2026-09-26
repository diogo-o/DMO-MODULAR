using DMO.Application.JobOn;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Tools;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Entities;
using DMO.Infrastructure.Persistence.EntityConfigurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using DomainJobOn = DMO.Domain.JobOn.JobOn;
using DomainJobOnId = DMO.Domain.JobOn.JobOnId;

namespace DMO.Infrastructure.Persistence.ToolJobOn;

/// <summary>
/// <see cref="IJobOnRepository"/> implementation over the single application persistence context.
/// </summary>
/// <remarks>
/// <para>
/// Every write that touches more than one row opens its own transaction: the Job On row plus its
/// context rows are one unit, and a failure at any step leaves neither a partial Job On nor a
/// half-created context set. Contexts are protected by the parent row's version — they carry no
/// version of their own.
/// </para>
/// <para>
/// Contextual Tool resolution (existence + type match) and the freezing of the context's
/// type/reference/lot triple happen <b>inside</b> the write transaction, so a concurrent change
/// cannot bypass them. Duplication follows the same snapshot path as creation (Owner
/// clarification, contract §23): the service builds every duplicated context from the CURRENT
/// canonical Tool row at duplication time, and the repository persists exactly the supplied
/// snapshots — it never clones the source context's frozen triple.
/// </para>
/// </remarks>
public sealed class JobOnRepository : IJobOnRepository
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the repository over the application persistence context.</summary>
    public JobOnRepository(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<DomainJobOn?> GetByIdAsync(Guid jobOnId, CancellationToken cancellationToken)
    {
        var entity = await JobOns
            .AsNoTracking()
            .FirstOrDefaultAsync(jobOn => jobOn.JobOnId == jobOnId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        return Project(entity, await LoadContextsAsync(jobOnId, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<DomainJobOn?> FindByProductionAsync(
        string reference,
        string productionNumber,
        CancellationToken cancellationToken)
    {
        var entity = await JobOns
            .AsNoTracking()
            .FirstOrDefaultAsync(
                jobOn => jobOn.Reference == reference && jobOn.ProductionNumber == productionNumber,
                cancellationToken);

        if (entity is null)
        {
            return null;
        }

        return Project(entity, await LoadContextsAsync(entity.JobOnId, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobOnProductionListItem>> ListByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        // The contracted deterministic technical order. PostgreSQL orders NULLs last for ASC, which
        // is exactly the contracted "production_date ASC NULLS LAST". The jobon_id tie-break makes
        // the order total, so repeated calls never reorder equivalent rows. The order carries no
        // industrial meaning and is never used to pre-select a production.
        return await JobOns
            .AsNoTracking()
            .Where(jobOn => jobOn.Reference == reference)
            .OrderBy(jobOn => jobOn.ProductionNumber)
            .ThenBy(jobOn => jobOn.ProductionDate)
            .ThenBy(jobOn => jobOn.JobOnId)
            .Select(jobOn => new JobOnProductionListItem(
                jobOn.JobOnId,
                jobOn.Reference,
                jobOn.ProductionNumber,
                jobOn.Machine,
                jobOn.ProductionDate))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DomainJobOn> CreatedAsync(
        DomainJobOn jobOn,
        IReadOnlyList<ToolContext> contexts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobOn);
        ArgumentNullException.ThrowIfNull(contexts);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var conflictId = await FindProductionIdAsync(
                jobOn.Reference,
                jobOn.ProductionNumber,
                cancellationToken);

            if (conflictId is { } existing)
            {
                await SafeRollbackAsync(transaction, cancellationToken);
                throw new JobOnPersistenceException(
                    JobOnPersistenceFailureReason.DuplicateProduction,
                    DuplicateProductionMessage(jobOn.Reference, jobOn.ProductionNumber),
                    existingJobOnId: existing);
            }

            JobOns.Add(new JobOnEntity
            {
                JobOnId = jobOn.JobOnId.Value,
                Reference = jobOn.Reference,
                ProductionNumber = jobOn.ProductionNumber,
                Machine = jobOn.Machine.Value,
                ProductionDate = jobOn.ProductionDate,
                CopiedFromJobOnId = null,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
            });

            // ONLY the slots the operator explicitly filled produce a context row: zero to three.
            foreach (var context in contexts)
            {
                await InsertContextFromLiveToolAsync(jobOn.JobOnId.Value, context, now, cancellationToken);
            }

            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryMapWriteFailure(exception, out var failure))
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            _context.ChangeTracker.Clear();

            var mapped = await EnrichAsync(failure, jobOn.Reference, jobOn.ProductionNumber, exception, cancellationToken);
            throw mapped;
        }

        return await GetByIdAsync(jobOn.JobOnId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The created Job On '{jobOn.JobOnId}' could not be read back after commit.");
    }

    /// <inheritdoc />
    public async Task<DomainJobOn> UpdatedAsync(
        DomainJobOn jobOn,
        IReadOnlyList<ToolContextChange> changes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobOn);
        ArgumentNullException.ThrowIfNull(changes);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await JobOns
                .FirstOrDefaultAsync(candidate => candidate.JobOnId == jobOn.JobOnId.Value, cancellationToken)
                ?? throw new ConcurrencyConflictException(
                    $"Job On '{jobOn.JobOnId}' no longer exists (deleted concurrently); nothing was written.");

            if (entity.Version != jobOn.Version)
            {
                throw new ConcurrencyConflictException(
                    $"Job On '{jobOn.JobOnId}' was modified concurrently (expected version {jobOn.Version}, " +
                    $"current version {entity.Version}); reload and retry.");
            }

            var conflictId = await FindProductionIdAsync(
                jobOn.Reference,
                jobOn.ProductionNumber,
                cancellationToken);

            if (conflictId is { } other && other != jobOn.JobOnId.Value)
            {
                await SafeRollbackAsync(transaction, cancellationToken);
                throw new JobOnPersistenceException(
                    JobOnPersistenceFailureReason.DuplicateProduction,
                    DuplicateProductionMessage(jobOn.Reference, jobOn.ProductionNumber),
                    existingJobOnId: other);
            }

            // The four editable facts and nothing else.
            entity.Reference = jobOn.Reference;
            entity.ProductionNumber = jobOn.ProductionNumber;
            entity.Machine = jobOn.Machine.Value;
            entity.ProductionDate = jobOn.ProductionDate;

            foreach (var change in changes)
            {
                switch (change.Action)
                {
                    case ToolAssociationAction.Keep:
                        // Keep writes nothing: "leave as is" is never conflated with "remove".
                        break;

                    case ToolAssociationAction.Set:
                        await ApplySetAsync(jobOn.JobOnId.Value, change, now, cancellationToken);
                        break;

                    case ToolAssociationAction.Remove:
                        await ApplyRemoveAsync(jobOn.JobOnId.Value, change.ContextType, cancellationToken);
                        break;
                }
            }

            // One version increment per committed mutation; the token stays active so a race between
            // this compare and the save still surfaces as a typed conflict.
            entity.Version += 1;
            entity.UpdatedAt = now;

            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryMapWriteFailure(exception, out var failure))
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            _context.ChangeTracker.Clear();

            var mapped = await EnrichAsync(failure, jobOn.Reference, jobOn.ProductionNumber, exception, cancellationToken);
            throw mapped;
        }

        return await GetByIdAsync(jobOn.JobOnId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The updated Job On '{jobOn.JobOnId}' could not be read back after commit.");
    }

    /// <inheritdoc />
    public async Task<DomainJobOn> DuplicatedAsync(
        DomainJobOn duplicate,
        IReadOnlyList<ToolContext> duplicatedContexts,
        int expectedSourceVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(duplicate);
        ArgumentNullException.ThrowIfNull(duplicatedContexts);

        var sourceJobOnId = duplicate.CopiedFromJobOnId
            ?? throw new InvalidOperationException(
                "A duplicated Job On always records its explicit source relation.");

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var source = await JobOns
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.JobOnId == sourceJobOnId, cancellationToken)
                ?? throw new ConcurrencyConflictException(
                    $"The duplication source '{sourceJobOnId}' no longer exists; nothing was created.");

            if (source.Version != expectedSourceVersion)
            {
                throw new ConcurrencyConflictException(
                    $"The duplication source '{sourceJobOnId}' changed after it was previewed (expected " +
                    $"version {expectedSourceVersion}, current version {source.Version}); nothing was created.");
            }

            var conflictId = await FindProductionIdAsync(
                duplicate.Reference,
                duplicate.ProductionNumber,
                cancellationToken);

            if (conflictId is { } existing)
            {
                await SafeRollbackAsync(transaction, cancellationToken);
                throw new JobOnPersistenceException(
                    JobOnPersistenceFailureReason.DuplicateProduction,
                    DuplicateProductionMessage(duplicate.Reference, duplicate.ProductionNumber),
                    existingJobOnId: existing);
            }

            JobOns.Add(new JobOnEntity
            {
                JobOnId = duplicate.JobOnId.Value,
                Reference = duplicate.Reference,
                ProductionNumber = duplicate.ProductionNumber,
                Machine = duplicate.Machine.Value,
                ProductionDate = duplicate.ProductionDate,
                CopiedFromJobOnId = sourceJobOnId,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
            });

            // New context identities; each supplied context carries the SOURCE context's canonical
            // tool_id and the snapshot the service produced from the CURRENT canonical Tool row at
            // duplication time (Owner clarification, contract §23 — the source context's frozen
            // triple is never cloned).
            foreach (var context in duplicatedContexts)
            {
                InsertContextCopy(duplicate.JobOnId.Value, context, now);
            }

            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryMapWriteFailure(exception, out var failure))
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            _context.ChangeTracker.Clear();

            var mapped = await EnrichAsync(failure, duplicate.Reference, duplicate.ProductionNumber, exception, cancellationToken);
            throw mapped;
        }

        return await GetByIdAsync(duplicate.JobOnId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The duplicated Job On '{duplicate.JobOnId}' could not be read back after commit.");
    }

    /// <inheritdoc />
    public async Task DeletedAsync(Guid jobOnId, int expectedVersion, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await JobOns
                .FirstOrDefaultAsync(candidate => candidate.JobOnId == jobOnId, cancellationToken)
                ?? throw new ConcurrencyConflictException(
                    $"Job On '{jobOnId}' no longer exists (deleted concurrently); nothing was deleted.");

            if (entity.Version != expectedVersion)
            {
                throw new ConcurrencyConflictException(
                    $"Job On '{jobOnId}' was modified concurrently (expected version {expectedVersion}, " +
                    $"current version {entity.Version}); nothing was deleted.");
            }

            // 1. This Job On's own context rows: loaded as tracked rows and removed explicitly, one
            //    context kind at a time, in a deterministic order — never a database cascade, and
            //    never anything belonging to another module. Removing the dependents FIRST keeps the
            //    relationship consistent in the change tracker.
            var cm = await _context.Set<CmContextEntity>()
                .Where(context => context.JobOnId == jobOnId)
                .ToListAsync(cancellationToken);
            if (cm.Count > 0)
            {
                _context.RemoveRange(cm);
            }

            var mf = await _context.Set<MfContextEntity>()
                .Where(context => context.JobOnId == jobOnId)
                .ToListAsync(cancellationToken);
            if (mf.Count > 0)
            {
                _context.RemoveRange(mf);
            }

            var bq = await _context.Set<BqContextEntity>()
                .Where(context => context.JobOnId == jobOnId)
                .ToListAsync(cancellationToken);
            if (bq.Count > 0)
            {
                _context.RemoveRange(bq);
            }

            // Contexts first, on their own save, so a dependent record's RESTRICT refusal fails
            // before the occurrence row is touched. The two steps share one transaction: a failure
            // in between leaves the Job On and its contexts intact (fail closed, never a partial
            // delete).
            if (cm.Count + mf.Count + bq.Count > 0)
            {
                await SaveAsync(cancellationToken);
            }

            // 2. Then the occurrence itself.
            JobOns.Remove(entity);

            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryMapWriteFailure(exception, out var failure))
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            throw failure;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobOnDependency>> ListLineageDependentsAsync(
        Guid jobOnId,
        CancellationToken cancellationToken)
    {
        var dependents = await JobOns
            .AsNoTracking()
            .Where(jobOn => jobOn.CopiedFromJobOnId == jobOnId)
            .OrderBy(jobOn => jobOn.Reference)
            .ThenBy(jobOn => jobOn.ProductionNumber)
            .Select(jobOn => new { jobOn.JobOnId, jobOn.Reference, jobOn.ProductionNumber })
            .ToListAsync(cancellationToken);

        return dependents
            .Select(dependent => new JobOnDependency(
                JobOnLineageDependencyProbe.DuplicationLineageKind,
                $"O Job On '{dependent.Reference}' / produção '{dependent.ProductionNumber}' foi " +
                "duplicado a partir deste Job On."))
            .ToList();
    }

    private DbSet<JobOnEntity> JobOns => _context.Set<JobOnEntity>();

    /// <summary>
    /// Saves the tracked changes, mapping a save-time optimistic-concurrency conflict onto the
    /// domain typed conflict (contract §15.1): the explicit in-transaction version compare closes
    /// already-stale requests, while the active <c>IsConcurrencyToken</c> guard closes the remaining
    /// race between that compare and this save. When another transaction commits a version bump in
    /// that window, EF raises <see cref="DbUpdateConcurrencyException"/> (the guarded write matches
    /// zero rows with no server error), which is surfaced here as
    /// <see cref="ConcurrencyConflictException"/> so the application layer translates it into
    /// <c>Refused(StaleVersion)</c> → HTTP 409 — never a silent overwrite and never a generic 500.
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

    private async Task ApplySetAsync(
        Guid jobOnId,
        ToolContextChange change,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Resolution + freezing inside the transaction: the frozen triple comes from the Tool row as
        // it is at this moment, and a Tool that vanished or changed family fails the whole edit.
        var tool = await _context.Set<ToolEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.ToolId == change.ToolId!.Value, cancellationToken)
            ?? throw new JobOnPersistenceException(
                JobOnPersistenceFailureReason.ToolNotFound,
                $"The supplied context Tool '{change.ToolId}' does not exist in the canonical registry.");

        var requiredToken = ToolTokens.ToToken(ToolTokens.RequiredToolType(change.ContextType));
        if (!string.Equals(tool.ToolType, requiredToken, StringComparison.Ordinal))
        {
            throw new JobOnPersistenceException(
                JobOnPersistenceFailureReason.ToolTypeMismatch,
                $"A {requiredToken} context cannot reference a {tool.ToolType} Tool.");
        }

        switch (change.ContextType)
        {
            case ToolContextType.Cm:
                var cm = await _context.Set<CmContextEntity>()
                    .FirstOrDefaultAsync(context => context.JobOnId == jobOnId, cancellationToken);
                if (cm is null)
                {
                    _context.Set<CmContextEntity>().Add(new CmContextEntity
                    {
                        CmId = Guid.NewGuid(),
                        JobOnId = jobOnId,
                        ToolId = tool.ToolId,
                        ToolType = tool.ToolType,
                        ToolReference = tool.Reference,
                        ToolLot = tool.Lot,
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                }
                else
                {
                    // In-place update: the SAME context row keeps its identity and the frozen triple is
                    // refreshed because a human explicitly re-selected the Tool for this slot.
                    cm.ToolId = tool.ToolId;
                    cm.ToolType = tool.ToolType;
                    cm.ToolReference = tool.Reference;
                    cm.ToolLot = tool.Lot;
                    cm.UpdatedAt = now;
                }

                break;

            case ToolContextType.Mf:
                var mf = await _context.Set<MfContextEntity>()
                    .FirstOrDefaultAsync(context => context.JobOnId == jobOnId, cancellationToken);
                if (mf is null)
                {
                    _context.Set<MfContextEntity>().Add(new MfContextEntity
                    {
                        MfId = Guid.NewGuid(),
                        JobOnId = jobOnId,
                        ToolId = tool.ToolId,
                        ToolType = tool.ToolType,
                        ToolReference = tool.Reference,
                        ToolLot = tool.Lot,
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                }
                else
                {
                    mf.ToolId = tool.ToolId;
                    mf.ToolType = tool.ToolType;
                    mf.ToolReference = tool.Reference;
                    mf.ToolLot = tool.Lot;
                    mf.UpdatedAt = now;
                }

                break;

            case ToolContextType.Bq:
                var bq = await _context.Set<BqContextEntity>()
                    .FirstOrDefaultAsync(context => context.JobOnId == jobOnId, cancellationToken);
                if (bq is null)
                {
                    _context.Set<BqContextEntity>().Add(new BqContextEntity
                    {
                        BqId = Guid.NewGuid(),
                        JobOnId = jobOnId,
                        ToolId = tool.ToolId,
                        ToolType = tool.ToolType,
                        ToolReference = tool.Reference,
                        ToolLot = tool.Lot,
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                }
                else
                {
                    bq.ToolId = tool.ToolId;
                    bq.ToolType = tool.ToolType;
                    bq.ToolReference = tool.Reference;
                    bq.ToolLot = tool.Lot;
                    bq.UpdatedAt = now;
                }

                break;
        }
    }

    private async Task ApplyRemoveAsync(
        Guid jobOnId,
        ToolContextType contextType,
        CancellationToken cancellationToken)
    {
        // Removal deletes ONLY that context row, inside the enclosing Job On transaction. Any future
        // dependent table's ON DELETE RESTRICT foreign key fails the delete closed.
        switch (contextType)
        {
            case ToolContextType.Cm:
                var cm = await _context.Set<CmContextEntity>()
                    .FirstOrDefaultAsync(context => context.JobOnId == jobOnId, cancellationToken);
                if (cm is not null)
                {
                    _context.Set<CmContextEntity>().Remove(cm);
                }

                break;

            case ToolContextType.Mf:
                var mf = await _context.Set<MfContextEntity>()
                    .FirstOrDefaultAsync(context => context.JobOnId == jobOnId, cancellationToken);
                if (mf is not null)
                {
                    _context.Set<MfContextEntity>().Remove(mf);
                }

                break;

            case ToolContextType.Bq:
                var bq = await _context.Set<BqContextEntity>()
                    .FirstOrDefaultAsync(context => context.JobOnId == jobOnId, cancellationToken);
                if (bq is not null)
                {
                    _context.Set<BqContextEntity>().Remove(bq);
                }

                break;
        }
    }

    private async Task InsertContextFromLiveToolAsync(
        Guid jobOnId,
        ToolContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var tool = await _context.Set<ToolEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.ToolId == context.ToolId.Value, cancellationToken)
            ?? throw new JobOnPersistenceException(
                JobOnPersistenceFailureReason.ToolNotFound,
                $"The supplied context Tool '{context.ToolId}' does not exist in the canonical registry.");

        var requiredToken = ToolTokens.ToToken(ToolTokens.RequiredToolType(context.ContextType));
        if (!string.Equals(tool.ToolType, requiredToken, StringComparison.Ordinal))
        {
            throw new JobOnPersistenceException(
                JobOnPersistenceFailureReason.ToolTypeMismatch,
                $"A {requiredToken} context cannot reference a {tool.ToolType} Tool.");
        }

        InsertContext(
            jobOnId,
            context.ContextType,
            context.ContextId,
            tool.ToolId,
            tool.ToolType,
            tool.Reference,
            tool.Lot,
            now);
    }

    private void InsertContextCopy(Guid jobOnId, ToolContext context, DateTimeOffset now) =>
        InsertContext(
            jobOnId,
            context.ContextType,
            context.ContextId,
            context.ToolId.Value,
            ToolTokens.ToToken(context.Frozen.Type),
            context.Frozen.Reference,
            context.Frozen.Lot,
            now);

    private void InsertContext(
        Guid jobOnId,
        ToolContextType contextType,
        Guid contextId,
        Guid toolId,
        string toolType,
        string toolReference,
        string toolLot,
        DateTimeOffset now)
    {
        switch (contextType)
        {
            case ToolContextType.Cm:
                _context.Set<CmContextEntity>().Add(new CmContextEntity
                {
                    CmId = contextId,
                    JobOnId = jobOnId,
                    ToolId = toolId,
                    ToolType = toolType,
                    ToolReference = toolReference,
                    ToolLot = toolLot,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                break;

            case ToolContextType.Mf:
                _context.Set<MfContextEntity>().Add(new MfContextEntity
                {
                    MfId = contextId,
                    JobOnId = jobOnId,
                    ToolId = toolId,
                    ToolType = toolType,
                    ToolReference = toolReference,
                    ToolLot = toolLot,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                break;

            case ToolContextType.Bq:
                _context.Set<BqContextEntity>().Add(new BqContextEntity
                {
                    BqId = contextId,
                    JobOnId = jobOnId,
                    ToolId = toolId,
                    ToolType = toolType,
                    ToolReference = toolReference,
                    ToolLot = toolLot,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                break;
        }
    }

    private async Task<IReadOnlyList<ToolContext>> LoadContextsAsync(
        Guid jobOnId,
        CancellationToken cancellationToken)
    {
        // The rows are read per context table and mapped in memory: the domain projection builds
        // value objects, which is deliberately not a translatable expression tree.
        var cm = await _context.Set<CmContextEntity>()
            .AsNoTracking()
            .Where(context => context.JobOnId == jobOnId)
            .Select(context => new ContextRow(
                context.CmId, context.JobOnId, context.ToolId, context.ToolReference, context.ToolLot))
            .ToListAsync(cancellationToken);

        var mf = await _context.Set<MfContextEntity>()
            .AsNoTracking()
            .Where(context => context.JobOnId == jobOnId)
            .Select(context => new ContextRow(
                context.MfId, context.JobOnId, context.ToolId, context.ToolReference, context.ToolLot))
            .ToListAsync(cancellationToken);

        var bq = await _context.Set<BqContextEntity>()
            .AsNoTracking()
            .Where(context => context.JobOnId == jobOnId)
            .Select(context => new ContextRow(
                context.BqId, context.JobOnId, context.ToolId, context.ToolReference, context.ToolLot))
            .ToListAsync(cancellationToken);

        return
        [
            .. cm.Select(row => ToContext(ToolContextType.Cm, row)),
            .. mf.Select(row => ToContext(ToolContextType.Mf, row)),
            .. bq.Select(row => ToContext(ToolContextType.Bq, row)),
        ];
    }

    private static ToolContext ToContext(ToolContextType contextType, ContextRow row) => new(
        contextType,
        row.ContextId,
        DomainJobOnId.From(row.JobOnId),
        ToolId.From(row.ToolId),
        new ToolContextSnapshot(contextType switch
        {
            ToolContextType.Cm => ToolType.Cm,
            ToolContextType.Mf => ToolType.Mf,
            _ => ToolType.Bq,
        }, row.ToolReference, row.ToolLot));

    private sealed record ContextRow(
        Guid ContextId,
        Guid JobOnId,
        Guid ToolId,
        string ToolReference,
        string ToolLot);

    private Task<Guid?> FindProductionIdAsync(
        string reference,
        string productionNumber,
        CancellationToken cancellationToken) =>
        JobOns
            .AsNoTracking()
            .Where(jobOn => jobOn.Reference == reference && jobOn.ProductionNumber == productionNumber)
            .Select(jobOn => (Guid?)jobOn.JobOnId)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<JobOnPersistenceException> EnrichAsync(
        JobOnPersistenceException failure,
        string reference,
        string productionNumber,
        Exception cause,
        CancellationToken cancellationToken)
    {
        if (failure.Reason != JobOnPersistenceFailureReason.DuplicateProduction ||
            failure.ExistingJobOnId is not null)
        {
            return failure;
        }

        // The unique index closed the race: read the winning occurrence so the refusal can name it.
        // An identity is never fabricated; when it cannot be read the honest duplicate refusal stands.
        var existing = await FindProductionIdAsync(reference, productionNumber, cancellationToken);

        return new JobOnPersistenceException(
            JobOnPersistenceFailureReason.DuplicateProduction,
            failure.Message,
            cause,
            existing);
    }

    private static bool TryMapWriteFailure(DbUpdateException exception, out JobOnPersistenceException failure)
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
            case "23505": // unique_violation
                if (string.Equals(
                        postgresException.ConstraintName,
                        JobOnEntityConfiguration.ReferenceProductionNumberUniqueConstraintName,
                        StringComparison.Ordinal))
                {
                    failure = new JobOnPersistenceException(
                        JobOnPersistenceFailureReason.DuplicateProduction,
                        "Another Job On already owns this reference and production number.",
                        exception);
                    return true;
                }

                break;

            case "23503": // foreign_key_violation — every P2-T04 foreign key is RESTRICT.
                failure = DependencyFailure(exception);
                return true;
        }

        return false;
    }

    private static JobOnPersistenceException DependencyFailure(Exception cause) =>
        new(
            JobOnPersistenceFailureReason.DependencyExists,
            "A dependent record still references this Job On or one of its contexts; nothing was deleted.",
            cause);

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

    private static string DuplicateProductionMessage(string reference, string productionNumber) =>
        $"A Job On with reference '{reference}' and production number '{productionNumber}' already " +
        "exists; select that production or duplicate it explicitly instead of creating a second one.";

    private static DomainJobOn Project(JobOnEntity entity, IReadOnlyList<ToolContext> contexts) => new(
        DomainJobOnId.From(entity.JobOnId),
        entity.Reference,
        entity.ProductionNumber,
        MachineCode.From(entity.Machine),
        entity.ProductionDate,
        entity.CopiedFromJobOnId,
        entity.Version,
        contexts);
}
