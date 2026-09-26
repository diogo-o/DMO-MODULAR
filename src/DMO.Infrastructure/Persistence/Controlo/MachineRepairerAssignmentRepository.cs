using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;
using DMO.Domain.Tools;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using DMO.Infrastructure.Persistence;
namespace DMO.Infrastructure.Persistence.Controlo;

/// <summary>
/// <see cref="IMachineRepairerAssignmentRepository"/> over the single application persistence
/// context.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §11/§16.4/§18. One row per machine, UNIQUE machine; every operation
/// touches exactly the ONE machine's row — never another machine's (MAC2–MAC4/AC-E2). An absent
/// row is the explicit "no repairer assigned" state.
/// </remarks>
public sealed class MachineRepairerAssignmentRepository : IMachineRepairerAssignmentRepository
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the repository over the application persistence context.</summary>
    public MachineRepairerAssignmentRepository(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<MachineRepairerAssignment?> GetByMachineAsync(
        string machine,
        CancellationToken cancellationToken)
    {
        var entity = await Assignments
            .AsNoTracking()
            .FirstOrDefaultAsync(assignment => assignment.Machine == machine, cancellationToken);

        return entity is null ? null : Project(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MachineRepairerAssignment>> ListAsync(CancellationToken cancellationToken)
    {
        // The settled machine order B1 B2 B3 C1 C2 C3 (deterministic technical order).
        var entities = await Assignments
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return entities
            .OrderBy(assignment => MachineCode.Parse(assignment.Machine)?.Order ?? int.MaxValue)
            .Select(Project)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<MachineRepairerAssignment> SetAsync(
        MachineRepairerAssignment assignment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await Assignments
                .FirstOrDefaultAsync(candidate => candidate.Machine == assignment.Machine.Value, cancellationToken);

            if (entity is null)
            {
                // First set: insert the row at version 1 (no observed version needed).
                Assignments.Add(new MachineRepairerAssignmentEntity
                {
                    MachineRepairerAssignmentId = assignment.MachineRepairerAssignmentId,
                    Machine = assignment.Machine.Value,
                    RepairerId = assignment.RepairerId.Value,
                    Version = 1,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            else
            {
                // Change: version-guarded in-place update of the SAME row (same machine key).
                if (entity.Version != assignment.Version)
                {
                    throw new ConcurrencyConflictException(
                        $"The assignment of machine '{assignment.Machine}' was modified concurrently " +
                        $"(expected version {assignment.Version}, current version {entity.Version}); " +
                        "reload and retry.");
                }

                entity.RepairerId = assignment.RepairerId.Value;
                entity.Version += 1;
                entity.UpdatedAt = now;
            }

            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryMapWriteFailure(exception, out var failure))
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            _context.ChangeTracker.Clear();
            throw failure;
        }

        return await GetByMachineAsync(assignment.Machine.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The assignment of machine '{assignment.Machine}' could not be read back after commit.");
    }

    /// <inheritdoc />
    public async Task ClearedAsync(string machine, int expectedVersion, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await Assignments
                .FirstOrDefaultAsync(candidate => candidate.Machine == machine, cancellationToken);

            if (entity is null)
            {
                // Already clear: the explicit empty state is preserved; nothing to remove.
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (entity.Version != expectedVersion)
            {
                throw new ConcurrencyConflictException(
                    $"The assignment of machine '{machine}' was modified concurrently (expected version " +
                    $"{expectedVersion}, current version {entity.Version}); reload and retry.");
            }

            Assignments.Remove(entity);

            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryMapWriteFailure(exception, out var failure))
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            _context.ChangeTracker.Clear();
            throw failure;
        }
    }

    private DbSet<MachineRepairerAssignmentEntity> Assignments => _context.Set<MachineRepairerAssignmentEntity>();

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

    private static bool TryMapWriteFailure(DbUpdateException exception, out ControloPersistenceException failure)
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
            case "23503": // FK_pesos-style repairer FK: the supplied repairer does not exist.
                failure = new ControloPersistenceException(
                    ControloPersistenceFailureReason.RepairerNotFound,
                    "The supplied repairer does not exist in the register; nothing was written.");
                return true;

            case "23514": // machine CHECK backstop (should be pre-empted by the validator).
                failure = new ControloPersistenceException(
                    ControloPersistenceFailureReason.RepairerNotFound,
                    "The supplied machine is not one of the six settled machines; nothing was written.");
                return true;

            default:
                return false;
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

    private static MachineRepairerAssignment Project(MachineRepairerAssignmentEntity entity) => new(
        entity.MachineRepairerAssignmentId,
        MachineCode.From(entity.Machine),
        RepairerId.From(entity.RepairerId),
        entity.Version,
        entity.CreatedAt,
        entity.UpdatedAt);
}