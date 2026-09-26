using DMO.Application.Accounts;
using DMO.Application.Authentication;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DMO.Infrastructure.Persistence.Access;

/// <summary>
/// Production, persistence-backed <see cref="IAccountLookup"/> (replaces the P1-T02
/// <c>UnavailableAccountLookup</c>).
/// </summary>
/// <remarks>
/// <para>
/// The lookup is the data boundary only: it returns candidate <see cref="AccountMatch"/>
/// entries from the persisted application mapping (<c>admin_accounts</c> / <c>users</c>
/// keyed by provider subject) and never classifies. <see cref="AccountResolver"/> remains the
/// sole account-classification authority.
/// </para>
/// <para>
/// Unknown provider subject → empty → the resolver fails closed
/// (<c>NoAccess(UnknownAccount)</c>). Duplicate <c>auth_identity_id</c> is structurally
/// impossible (UNIQUE constraint), so ambiguity cannot be produced by storage.
/// </para>
/// </remarks>
public sealed class PersistenceAccountLookup : IAccountLookup
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the lookup over the single application persistence context.</summary>
    public PersistenceAccountLookup(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountMatch>> LookupAsync(
        AuthenticatedIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (identity.AuthenticationPath == AuthenticationPath.Admin)
        {
            var admin = await _context.AdminAccounts
                .Where(account => account.AuthIdentityId == identity.ProviderSubject)
                .Select(account => new AdminAccount(
                    account.AdminId,
                    account.DisplayName,
                    account.Email,
                    account.Active))
                .FirstOrDefaultAsync(cancellationToken);

            return admin is null
                ? []
                : new AccountMatch[] { new AccountMatch.Admin(admin) };
        }

        var user = await _context.Users
            .Where(account => account.AuthIdentityId == identity.ProviderSubject)
            .Select(account => new UserAccount(
                account.UserId,
                account.CompanyNumber,
                account.Name,
                account.Email,
                account.Role ?? string.Empty,
                account.Active,
                account.TemplateId,
                account.Version))
            .FirstOrDefaultAsync(cancellationToken);

        return user is null
            ? []
            : new AccountMatch[] { new AccountMatch.User(user) };
    }
}