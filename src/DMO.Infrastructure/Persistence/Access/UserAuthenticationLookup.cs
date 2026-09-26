using DMO.Application.Authentication;
using DMO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DMO.Infrastructure.Persistence.Access;

/// <summary>
/// <see cref="IUserAuthenticationLookup"/> implementation against <c>users</c>.
/// </summary>
/// <remarks>
/// <para>
/// Resolves the provider credential carrier email for the canonical USER login identifier.
/// <c>company_number</c> is matched exactly against the unique <c>users.company_number</c>;
/// the returned <see cref="UserLoginIdentity.CarrierEmail"/> is a provisioned account
/// attribute (<c>users.email</c>, NOT NULL), never a synthetic email and never the
/// <c>company_number</c> itself.
/// </para>
/// <para>
/// This is the narrow persistence seam consumed by the authentication boundary; the Web
/// authentication adapter never touches EF directly.
/// </para>
/// </remarks>
public sealed class UserAuthenticationLookup : IUserAuthenticationLookup
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the lookup over the single application persistence context.</summary>
    public UserAuthenticationLookup(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<UserLoginIdentity?> GetByCompanyNumberAsync(
        string companyNumber,
        CancellationToken cancellationToken)
    {
        var carrierEmail = await _context.Users
            .Where(user => user.CompanyNumber == companyNumber)
            .Select(user => (string?)user.Email)
            .FirstOrDefaultAsync(cancellationToken);

        // One row at most (company_number UNIQUE). NOT NULL schema-wise, but the null-safe
        // shape keeps the contract honest for an unknown company number (no row -> null).
        return carrierEmail is null
            ? null
            : new UserLoginIdentity(carrierEmail);
    }
}