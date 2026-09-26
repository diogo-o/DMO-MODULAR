using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using DomainRepairerId = DMO.Domain.Controlo.RepairerId;

using DMO.Infrastructure.Persistence;
namespace DMO.Infrastructure.Persistence.Controlo;

/// <summary>
/// <see cref="IRepairerRepository"/> over the single application persistence context.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §10/§16.3/§18. There is no delete path (REP3/AC-D3); a repairer
/// referenced by a machine assignment is DB-protected by the RESTRICT FK (REP5/AC-D3).
/// </remarks>
public sealed class RepairerRepository : IRepairerRepository
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the repository over the application persistence context.</summary>
    public RepairerRepository(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<Repairer?> GetByIdAsync(Guid repairerId, CancellationToken cancellationToken)
    {
        var entity = await Repairers
            .AsNoTracking()
            .FirstOrDefaultAsync(repairer => repairer.RepairerId == repairerId, cancellationToken);

        return entity is null ? null : Project(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Repairer>> ListAsync(CancellationToken cancellationToken)
    {
        // Deterministic technical order; no ranking meaning (§10).
        var entities = await Repairers
            .AsNoTracking()
            .OrderBy(repairer => repairer.Name)
            .ThenBy(repairer => repairer.RepairerId)
            .ToListAsync(cancellationToken);

        return entities.Select(Project).ToList();
    }

    /// <inheritdoc />
    public async Task<Repairer> CreatedAsync(Repairer repairer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repairer);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            Repairers.Add(new RepairerEntity
            {
                RepairerId = repairer.RepairerId.Value,
                Name = repairer.Name.Trim(),
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
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

        return await GetByIdAsync(repairer.RepairerId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The created repairer '{repairer.RepairerId}' could not be read back after commit.");
    }

    /// <inheritdoc />
    public async Task<Repairer> RenamedAsync(Repairer repairer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repairer);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await Repairers
                .FirstOrDefaultAsync(candidate => candidate.RepairerId == repairer.RepairerId.Value, cancellationToken)
                ?? throw new ConcurrencyConflictException(
                    $"Repairer '{repairer.RepairerId}' no longer exists; nothing was written.");

            if (entity.Version != repairer.Version)
            {
                throw new ConcurrencyConflictException(
                    $"Repairer '{repairer.RepairerId}' was modified concurrently (expected version " +
                    $"{repairer.Version}, current version {entity.Version}); reload and retry.");
            }

            // The same repairer_id is retained: rename edits the name only (AC-D2).
            entity.Name = repairer.Name.Trim();
            entity.Version += 1;
            entity.UpdatedAt = now;

            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryMapWriteFailure(exception, out var failure))
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            _context.ChangeTracker.Clear();
            throw failure;
        }

        return await GetByIdAsync(repairer.RepairerId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The renamed repairer '{repairer.RepairerId}' could not be read back after commit.");
    }

    private DbSet<RepairerEntity> Repairers => _context.Set<RepairerEntity>();

    /// <summary>
    /// Saves the tracked changes, mapping a save-time optimistic-concurrency conflict onto the
    /// domain typed conflict (contract §19.1, accepted <c>SaveAsync</c> pattern).
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

    private static bool TryMapWriteFailure(DbUpdateException exception, out ControloPersistenceException failure)
    {
        failure = null!;

        var postgresException = exception.InnerException as Npgsql.PostgresException
            ?? exception.InnerException?.InnerException as Npgsql.PostgresException;

        if (postgresException is null)
        {
            return false;
        }

        // 23503: a dependent row (machine assignment) references the repairer — RESTRICT backstop.
        if (postgresException.SqlState == "23503")
        {
            failure = new ControloPersistenceException(
                ControloPersistenceFailureReason.DependencyExists,
                "A machine assignment still references this repairer; nothing was deleted.",
                exception);
            return true;
        }

        return false;
    }

    private static Repairer Project(RepairerEntity entity) => new(
        DomainRepairerId.From(entity.RepairerId),
        entity.Name,
        entity.Version,
        entity.CreatedAt,
        entity.UpdatedAt);
}