using DMO.Application.Migrations;
using Microsoft.EntityFrameworkCore;

namespace DMO.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of the migration-runner boundary.
/// </summary>
/// <remarks>
/// EF Core migrations are used as the migration mechanism. At P1-T01 the migrations
/// assembly is intentionally empty of product migrations, so a run applies nothing and
/// leaves the database schema untouched — exactly the accepted Phase 1 starting point.
/// </remarks>
public sealed class EfCoreMigrationRunner : IMigrationRunner
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the runner over the application persistence context.</summary>
    public EfCoreMigrationRunner(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<MigrationResult> ApplyPendingAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _context.Database.GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);
        var pendingList = pending.ToList();

        if (pendingList.Count == 0)
        {
            return MigrationResult.None;
        }

        await _context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        var stillPending = await _context.Database.GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        return new MigrationResult(pendingList.Count, stillPending.Count(), pendingList);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _context.Database.GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        return pending.ToList();
    }
}
