using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;
using DMO.Infrastructure.Persistence.Entities;
using DMO.Infrastructure.Persistence.EntityConfigurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using DomainEmailListId = DMO.Domain.Controlo.EmailListId;

using DMO.Infrastructure.Persistence;
namespace DMO.Infrastructure.Persistence.Controlo;

/// <summary>
/// <see cref="IEmailListRepository"/> over the single application persistence context.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §13/§16.6/§16.7/§18. A list update carries the <b>complete</b>
/// recipient set and replaces it atomically inside one transaction — no partial list state is ever
/// observable (SET4/SET5/AC-F3). The parent list's version protects the recipient set. The
/// <c>email_lists_name_key</c> collision maps to <c>duplicate-name</c>; a delete blocked by a
/// future dependent (RESTRICT backstop) maps to <c>dependency-exists</c>.
/// </remarks>
public sealed class EmailListRepository : IEmailListRepository
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the repository over the application persistence context.</summary>
    public EmailListRepository(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<EmailList?> GetByIdAsync(Guid emailListId, CancellationToken cancellationToken)
    {
        var entity = await Lists
            .AsNoTracking()
            .FirstOrDefaultAsync(list => list.EmailListId == emailListId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var recipients = await Recipients
            .AsNoTracking()
            .Where(recipient => recipient.EmailListId == emailListId)
            .OrderBy(recipient => recipient.Address)
            .ToListAsync(cancellationToken);

        return Project(entity, recipients);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailList>> ListAsync(CancellationToken cancellationToken)
    {
        // Registry-size read with the complete recipient sets (the count is derived from the set,
        // never a second stored fact). Deterministic technical order: name ASC, id tie-break.
        var entities = await Lists
            .AsNoTracking()
            .OrderBy(list => list.Name, StringComparer.Ordinal)
            .ThenBy(list => list.EmailListId)
            .ToListAsync(cancellationToken);

        if (entities.Count == 0)
        {
            return [];
        }

        var ids = entities.Select(list => list.EmailListId).ToList();
        var recipientRows = await Recipients
            .AsNoTracking()
            .Where(recipient => ids.Contains(recipient.EmailListId))
            .OrderBy(recipient => recipient.Address)
            .ToListAsync(cancellationToken);

        var recipientGroups = recipientRows.GroupBy(recipient => recipient.EmailListId);

        return entities
            .Select(entity => Project(entity, recipientGroups
                .Where(group => group.Key == entity.EmailListId)
                .SelectMany(group => group)
                .ToList()))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<EmailList> CreatedAsync(
        EmailList list,
        IReadOnlyList<EmailRecipient> recipients,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(recipients);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            Lists.Add(new EmailListEntity
            {
                EmailListId = list.EmailListId.Value,
                Name = list.Name.Trim(),
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
            });

            foreach (var recipient in recipients)
            {
                InsertRecipient(list.EmailListId.Value, recipient, now);
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

        return await GetByIdAsync(list.EmailListId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The created email list '{list.EmailListId}' could not be read back after commit.");
    }

    /// <inheritdoc />
    public async Task<EmailList> UpdatedAsync(
        EmailList list,
        IReadOnlyList<EmailRecipient> recipients,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(recipients);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await Lists
                .FirstOrDefaultAsync(candidate => candidate.EmailListId == list.EmailListId.Value, cancellationToken)
                ?? throw new ConcurrencyConflictException(
                    $"Email list '{list.EmailListId}' no longer exists; nothing was written.");

            if (entity.Version != list.Version)
            {
                throw new ConcurrencyConflictException(
                    $"Email list '{list.EmailListId}' was modified concurrently (expected version " +
                    $"{list.Version}, current version {entity.Version}); reload and retry.");
            }

            entity.Name = list.Name.Trim();
            entity.Version += 1;
            entity.UpdatedAt = now;

            // Replace-all: the complete recipient set is swapped in one transaction (SET5/AC-F3).
            var existing = await Recipients
                .Where(recipient => recipient.EmailListId == list.EmailListId.Value)
                .ToListAsync(cancellationToken);
            if (existing.Count > 0)
            {
                Recipients.RemoveRange(existing);
            }

            foreach (var recipient in recipients)
            {
                InsertRecipient(list.EmailListId.Value, recipient, now);
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

        return await GetByIdAsync(list.EmailListId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The updated email list '{list.EmailListId}' could not be read back after commit.");
    }

    /// <inheritdoc />
    public async Task DeletedAsync(Guid emailListId, int expectedVersion, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await Lists
                .FirstOrDefaultAsync(candidate => candidate.EmailListId == emailListId, cancellationToken)
                ?? throw new ConcurrencyConflictException(
                    $"Email list '{emailListId}' no longer exists; nothing was deleted.");

            if (entity.Version != expectedVersion)
            {
                throw new ConcurrencyConflictException(
                    $"Email list '{emailListId}' was modified concurrently (expected version " +
                    $"{expectedVersion}, current version {entity.Version}); nothing was deleted.");
            }

            // Recipients first (this list's own children), then the list row — one transaction.
            var recipients = await Recipients
                .Where(recipient => recipient.EmailListId == emailListId)
                .ToListAsync(cancellationToken);
            if (recipients.Count > 0)
            {
                Recipients.RemoveRange(recipients);
                await SaveAsync(cancellationToken);
            }

            Lists.Remove(entity);

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

    private DbSet<EmailListEntity> Lists => _context.Set<EmailListEntity>();

    private DbSet<EmailListRecipientEntity> Recipients => _context.Set<EmailListRecipientEntity>();

    private void InsertRecipient(Guid emailListId, EmailRecipient recipient, DateTimeOffset now)
    {
        Recipients.Add(new EmailListRecipientEntity
        {
            EmailListRecipientId = recipient.EmailListRecipientId,
            EmailListId = emailListId,
            Address = recipient.Address.Trim(),
            CreatedAt = now,
        });
    }

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
            case "23505": // unique_violation — the unique list name or a duplicate address in the set.
                if (string.Equals(
                        postgresException.ConstraintName,
                        EmailListEntityConfiguration.NameUniqueConstraintName,
                        StringComparison.Ordinal))
                {
                    failure = new ControloPersistenceException(
                        ControloPersistenceFailureReason.DuplicateListName,
                        "Another email list already owns this name.",
                        exception);
                    return true;
                }

                if (string.Equals(
                        postgresException.ConstraintName,
                        EmailListRecipientEntityConfiguration.ListAddressUniqueConstraintName,
                        StringComparison.Ordinal))
                {
                    // Backstop of the validator's one-address-once-per-list rule (SET6/AC-F4).
                    failure = new ControloPersistenceException(
                        ControloPersistenceFailureReason.DuplicateAddress,
                        "An address appears more than once in this list.",
                        exception);
                    return true;
                }

                break;

            case "23503": // RESTRICT backstop against a future dependent (P2-T08 routing).
                failure = new ControloPersistenceException(
                    ControloPersistenceFailureReason.DependencyExists,
                    "A dependent record still references this email list; nothing was deleted.",
                    exception);
                return true;
        }

        return false;
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

    private static EmailList Project(
        EmailListEntity entity,
        IReadOnlyList<EmailListRecipientEntity> recipients) => new(
        DomainEmailListId.From(entity.EmailListId),
        entity.Name,
        entity.Version,
        entity.CreatedAt,
        entity.UpdatedAt,
        recipients
            .Select(recipient => new EmailRecipient(
                recipient.EmailListRecipientId,
                DomainEmailListId.From(recipient.EmailListId),
                recipient.Address,
                recipient.CreatedAt))
            .ToList());
}