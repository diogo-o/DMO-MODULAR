using DMO.Application.Accounts;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Entities;
using DMO.Infrastructure.Persistence.EntityConfigurations;
using Microsoft.EntityFrameworkCore;

namespace DMO.Infrastructure.Persistence;

/// <summary>
/// <see cref="IUserRepository"/> implementation over the single application persistence
/// context.
/// </summary>
/// <remarks>
/// <para>
/// P1-T03 provides persistence primitives only; P1-T05 builds the USER administration
/// workflows on top. Write paths are optimistic: <c>version</c> is compared and incremented,
/// and a stale write surfaces as <see cref="ConcurrencyConflictException"/> — never a silent
/// overwrite.
/// </para>
/// </remarks>
public sealed class UserRepository : IUserRepository
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the repository over the application persistence context.</summary>
    public UserRepository(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<UserAccount?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
        => Project(_context.Users.Where(user => user.UserId == userId))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<UserAccount?> GetByCompanyNumberAsync(string companyNumber, CancellationToken cancellationToken)
        => Project(_context.Users.Where(user => user.CompanyNumber == companyNumber))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<UserAccount?> GetByAuthIdentityAsync(string providerSubject, CancellationToken cancellationToken)
        => Project(_context.Users.Where(user => user.AuthIdentityId == providerSubject))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserAccount>> ListActiveAsync(CancellationToken cancellationToken)
        => await Project(_context.Users.Where(user => user.Active))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task CreatedAsync(
        UserAccount account,
        string authIdentityId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(authIdentityId);

        var now = DateTimeOffset.UtcNow;
        _context.Users.Add(new UserEntity
        {
            UserId = account.AccountId,
            AuthIdentityId = authIdentityId,
            CompanyNumber = account.CompanyNumber,
            Name = account.DisplayName,
            Email = account.Email,
            Role = account.RoleLabel,
            Active = account.IsActive,
            TemplateId = account.TemplateId,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await SaveAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdatedAsync(UserAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        // Load the current row: the persisted version read here is the version the caller's
        // observation is compared against. A stale observation fails the compare and is
        // rejected before any field is changed.
        var entity = await _context.Users
            .SingleOrDefaultAsync(user => user.UserId == account.AccountId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"USER account '{account.AccountId}' was not found (delete or invalid identifier).");

        if (entity.Version != account.Version)
        {
            throw new ConcurrencyConflictException(
                $"USER account '{account.AccountId}' was modified concurrently (expected version {account.Version}, " +
                $"current version {entity.Version}); reload and retry.");
        }

        // The provider mapping (auth_identity_id) is durable linkage and is not changed here.
        entity.CompanyNumber = account.CompanyNumber;
        entity.Name = account.DisplayName;
        entity.Email = account.Email;
        entity.Role = account.RoleLabel;
        entity.Active = account.IsActive;
        entity.TemplateId = account.TemplateId;

        // Every committed write bumps version exactly once. The EF concurrency token stays
        // active: any race between the read/compare above and this save still surfaces as a
        // typed conflict (zero rows matched -> DbUpdateConcurrencyException -> domain conflict)
        // instead of silently overwriting the concurrent change.
        entity.Version += 1;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await SaveAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetActiveAsync(
        Guid userId,
        bool active,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var entity = await LoadTrackedAsync(userId, cancellationToken);

        if (entity.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                $"USER account '{userId}' was modified concurrently (expected version {expectedVersion}, " +
                $"current version {entity.Version}); reload and retry.");
        }

        entity.Active = active;
        entity.Version += 1;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await SaveAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetTemplateAsync(
        Guid userId,
        Guid? templateId,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var entity = await LoadTrackedAsync(userId, cancellationToken);

        if (entity.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                $"USER account '{userId}' was modified concurrently (expected version {expectedVersion}, " +
                $"current version {entity.Version}); reload and retry.");
        }

        entity.TemplateId = templateId;
        entity.Version += 1;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await SaveAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var entity = await _context.Users
            .SingleOrDefaultAsync(user => user.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"USER account '{userId}' was not found (delete or invalid identifier).");

        _context.Users.Remove(entity);

        await SaveAsync(cancellationToken);
    }

    // ---- P1-T05 additive reads/writes (no schema impact) -------------------------------

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken)
    {
        // Order at the entity level BEFORE the domain projection so the ordering translates.
        var ordered = _context.Users
            .OrderBy(user => user.Name)
            .ThenBy(user => user.CompanyNumber);

        return await Project(ordered).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserAccount>> ListByTemplateAsync(Guid templateId, CancellationToken cancellationToken)
    {
        // Same ordering convention as ListAsync; order at the entity level BEFORE the domain
        // projection so the ordering translates, and read the single users.template_id relation.
        var ordered = _context.Users
            .Where(user => user.TemplateId == templateId)
            .OrderBy(user => user.Name)
            .ThenBy(user => user.CompanyNumber);

        return await Project(ordered).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<UserAccount?> GetByEmailAsync(string email, CancellationToken cancellationToken)
        => Project(_context.Users.Where(user => user.Email == email))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<string?> GetAuthIdentityIdAsync(Guid userId, CancellationToken cancellationToken)
        => await _context.Users
            .Where(user => user.UserId == userId)
            .Select(user => (string?)user.AuthIdentityId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task DeleteAsync(Guid userId, int expectedVersion, CancellationToken cancellationToken)
    {
        // Version-compare + delete atomically: a stale concurrent write matches zero rows and
        // is never silently deleted. The explicit WHERE carries the expected version (the
        // version is the same concurrency boundary the other write primitives use).
        var deleted = await _context.Users
            .Where(user => user.UserId == userId && user.Version == expectedVersion)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
        {
            var exists = await _context.Users.AnyAsync(user => user.UserId == userId, cancellationToken);
            if (exists)
            {
                throw new ConcurrencyConflictException(
                    $"USER account '{userId}' was modified concurrently (expected version {expectedVersion}); " +
                    "reload and retry.");
            }

            // No row: it was already deleted concurrently. The caller interprets this state
            // (the P1-T05 delete flow treats it as the operation logically completing).
        }
    }

    private async Task<UserEntity> LoadTrackedAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.Users
            .SingleOrDefaultAsync(user => user.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"USER account '{userId}' was not found (delete or invalid identifier).");
    }

    private static IQueryable<UserAccount> Project(IQueryable<UserEntity> source) =>
        source.Select(user => new UserAccount(
            user.UserId,
            user.CompanyNumber,
            user.Name,
            user.Email,
            user.Role ?? string.Empty,
            user.Active,
            user.TemplateId,
            user.Version));

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
        catch (DbUpdateException exception) when (TryMapConstraintViolation(exception, out var failure))
        {
            throw failure;
        }
    }

    /// <summary>
    /// Maps the database constraint violations the P1-T05 flows can hit onto the typed
    /// <see cref="UserPersistenceException"/> so Application services can drive the accepted
    /// compensation posture without referencing EF/Npgsql.
    /// </summary>
    /// <remarks>
    /// Only <c>users</c> constraint failures are mapped (unique company number, unique provider
    /// subject, Template FK). Everything else propagates unchanged. Constraint names are the
    /// explicit database names from <see cref="UserEntityConfiguration"/>.
    /// </remarks>
    private static bool TryMapConstraintViolation(
        DbUpdateException exception,
        out UserPersistenceException failure)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var postgresException = exception.InnerException as Npgsql.PostgresException
            ?? exception.InnerException?.InnerException as Npgsql.PostgresException;

        if (postgresException is null)
        {
            failure = null!;
            return false;
        }

        switch (postgresException.SqlState)
        {
            case "23505": // unique_violation
                if (string.Equals(
                        postgresException.ConstraintName,
                        UserEntityConfiguration.CompanyNumberUniqueConstraintName,
                        StringComparison.Ordinal))
                {
                    failure = new UserPersistenceException(
                        UserPersistenceFailureReason.DuplicateCompanyNumber,
                        "The company number already exists; the USER write was rejected.",
                        exception);
                    return true;
                }

                if (string.Equals(
                        postgresException.ConstraintName,
                        UserEntityConfiguration.AuthIdentityIdUniqueConstraintName,
                        StringComparison.Ordinal))
                {
                    failure = new UserPersistenceException(
                        UserPersistenceFailureReason.DuplicateProviderSubject,
                        "The provider subject is already mapped to a USER; the write was rejected.",
                        exception);
                    return true;
                }

                break;

            case "23503": // foreign_key_violation — the only FK on users is template_id.
                failure = new UserPersistenceException(
                    UserPersistenceFailureReason.InvalidTemplateReference,
                    "The Template reference does not exist; the USER write was rejected.",
                    exception);
                return true;
        }

        failure = null!;
        return false;
    }
}