using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using DMO.Infrastructure.Persistence;
namespace DMO.Infrastructure.Persistence.Controlo;

/// <summary>
/// <see cref="IGlassDensitySettingsRepository"/> over the single application persistence
/// context.
/// </summary>
/// <remarks>
/// Authority: P2-T05 post-closure glass-density correction contract §5.1/§5.4 (baseline §20.2
/// pattern). One row per canonical processo (PK); every update touches EXACTLY the one
/// processo's row — never the other one — version-guarded, in one transaction: a mismatch
/// throws <see cref="ConcurrencyConflictException"/> (<c>stale-version</c> 409, nothing
/// written). Both rows are always seeded; an absent row is defensive only and never invented.
/// </remarks>
public sealed class GlassDensitySettingsRepository : IGlassDensitySettingsRepository
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the repository over the application persistence context.</summary>
    public GlassDensitySettingsRepository(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<GlassDensitySetting?> GetByProcessoAsync(
        string processo,
        CancellationToken cancellationToken)
    {
        var entity = await Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(setting => setting.Processo == processo, cancellationToken);

        return entity is null ? null : Project(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GlassDensitySetting>> ListAsync(CancellationToken cancellationToken)
    {
        var entities = await Settings
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return entities
            .OrderBy(setting => setting.Processo, StringComparer.Ordinal)
            .Select(Project)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<GlassDensitySetting> UpdatedAsync(
        GlassDensitySetting setting,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setting);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // The write targets EXACTLY this one processo's row (per-processo independence).
            var entity = await Settings
                .FirstOrDefaultAsync(candidate => candidate.Processo == setting.Processo, cancellationToken);

            if (entity is null)
            {
                throw new ConcurrencyConflictException(
                    $"The glass-density setting of processo '{setting.Processo}' no longer exists; " +
                    "reload and retry.");
            }

            if (entity.Version != setting.Version)
            {
                throw new ConcurrencyConflictException(
                    $"The glass-density setting of processo '{setting.Processo}' was modified " +
                    $"concurrently (expected version {setting.Version}, current version {entity.Version}); " +
                    "reload and retry.");
            }

            entity.DensityGCm3 = setting.DensityGCm3;
            entity.Version += 1;
            entity.UpdatedAt = now;

            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            _context.ChangeTracker.Clear();
            throw;
        }

        return await GetByProcessoAsync(setting.Processo, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The glass-density setting of processo '{setting.Processo}' could not be read " +
                "back after commit.");
    }

    private DbSet<GlassDensitySettingEntity> Settings => _context.Set<GlassDensitySettingEntity>();

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
        catch (DbUpdateException exception) when (TryMapWriteFailure(exception, out var failure))
        {
            throw failure;
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
            case "23514": // a CHECK backstop (should be pre-empted by the validator).
                failure = new ControloPersistenceException(
                    ControloPersistenceFailureReason.ResultNonPositive,
                    "The supplied density violates the glass-density setting constraints; " +
                    "nothing was written.");
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

    private static GlassDensitySetting Project(GlassDensitySettingEntity entity) => new(
        entity.Processo,
        entity.DensityGCm3,
        entity.Version,
        entity.CreatedAt,
        entity.UpdatedAt);
}