using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using DMO.Infrastructure.Persistence;
namespace DMO.Infrastructure.Persistence.Controlo;

/// <summary>
/// <see cref="IPdfDirectorySettingsRepository"/> over the single application persistence context.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §12.2/§16.5/§18. The single-row mechanism keeps at most one
/// configured base directory; the setting change is a plain setting change — no migration of
/// documents and no availability change (P2-T08 consumes the new value).
/// </remarks>
public sealed class PdfDirectorySettingsRepository : IPdfDirectorySettingsRepository
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the repository over the application persistence context.</summary>
    public PdfDirectorySettingsRepository(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<PdfDirectorySettings?> GetAsync(CancellationToken cancellationToken)
    {
        var entity = await Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : Project(entity);
    }

    /// <inheritdoc />
    public async Task<PdfDirectorySettings> SetAsync(
        PdfDirectorySettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await Settings
                .FirstOrDefaultAsync(cancellationToken);

            if (entity is null)
            {
                Settings.Add(new PdfDirectorySettingsEntity
                {
                    PdfDirectorySettingId = settings.PdfDirectorySettingId,
                    Singleton = true,
                    BaseDirectory = settings.BaseDirectory.Trim(),
                    Version = 1,
                    UpdatedAt = now,
                });
            }
            else
            {
                if (entity.Version != settings.Version)
                {
                    throw new ConcurrencyConflictException(
                        "The PDF directory setting was modified concurrently (expected version " +
                        $"{settings.Version}, current version {entity.Version}); reload and retry.");
                }

                entity.BaseDirectory = settings.BaseDirectory.Trim();
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

        return await GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("The PDF directory setting could not be read back after commit.");
    }

    private DbSet<PdfDirectorySettingsEntity> Settings => _context.Set<PdfDirectorySettingsEntity>();

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

        failure = new ControloPersistenceException(
            ControloPersistenceFailureReason.DependencyExists,
            "The PDF directory setting write was refused; nothing was written.",
            exception);

        return true;
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

    private static PdfDirectorySettings Project(PdfDirectorySettingsEntity entity) => new(
        entity.PdfDirectorySettingId,
        entity.BaseDirectory,
        entity.Version,
        entity.UpdatedAt);
}