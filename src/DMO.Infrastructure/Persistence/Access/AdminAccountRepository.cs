using DMO.Application.Accounts;
using DMO.Application.Repositories;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DMO.Infrastructure.Persistence.Access;

/// <summary>
/// <see cref="IAdminAccountRepository"/> implementation over the single application
/// persistence context.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no general ADMIN CRUD: only the single-account read paths and the
/// bootstrap create. The single-ADMIN invariant is enforced by the database (the
/// <c>admin_accounts_singleton_id_check</c> CHECK plus the primary key on the fixed
/// <see cref="AdminAccountId"/>); a duplicate insert from a concurrent bootstrap therefore
/// fails at the database layer.
/// </para>
/// </remarks>
public sealed class AdminAccountRepository : IAdminAccountRepository
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the repository over the application persistence context.</summary>
    public AdminAccountRepository(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<AdminAccount?> GetByAuthIdentityAsync(string providerSubject, CancellationToken cancellationToken)
        => Project(_context.AdminAccounts.Where(account => account.AuthIdentityId == providerSubject))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<AdminAccount?> GetByEmailAsync(string email, CancellationToken cancellationToken)
        => Project(_context.AdminAccounts.Where(account => account.Email == email))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<AdminAccount?> GetSingleAsync(CancellationToken cancellationToken)
    {
        // Exactly one row by the DB invariant (CHECK + PK). A second row is structurally
        // impossible, but the read still guards against it for clear diagnostics.
        var rows = await Project(_context.AdminAccounts)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (rows.Count > 1)
        {
            throw new InvalidOperationException(
                "More than one ADMIN row exists; the single-ADMIN database invariant was violated.");
        }

        return rows.Count == 0 ? null : rows[0];
    }

    /// <inheritdoc />
    public async Task CreatedAsync(
        AdminAccount account,
        string authIdentityId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.AccountId != AdminAccountId.Value)
        {
            throw new ArgumentException(
                $"The ADMIN row id must be the fixed {nameof(AdminAccountId)} constant " +
                $"({AdminAccountId.Value}); a different id can never exist.",
                nameof(account));
        }

        var now = DateTimeOffset.UtcNow;
        _context.AdminAccounts.Add(new AdminAccountEntity
        {
            AdminId = account.AccountId,
            AuthIdentityId = authIdentityId,
            DisplayName = account.DisplayName,
            Email = account.Email,
            Active = account.IsActive,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });

        // A duplicate insert (e.g. two concurrent bootstrap runs) fails here at the database
        // layer (PK / CHECK / unique constraints) regardless of application checks.
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static IQueryable<AdminAccount> Project(IQueryable<AdminAccountEntity> source) =>
        source.Select(account => new AdminAccount(
            account.AdminId,
            account.DisplayName,
            account.Email,
            account.Active));
}