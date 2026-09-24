using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;
using DMO.Infrastructure.Persistence.Entities;
using DMO.Infrastructure.Persistence.EntityConfigurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using DomainEmailTemplateId = DMO.Domain.Controlo.EmailTemplateId;

namespace DMO.Infrastructure.Persistence;

/// <summary>
/// <see cref="IEmailTemplateRepository"/> over the single application persistence context.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §14/§16.8/§18. Body text is stored verbatim — no placeholder
/// parsing (Q-PLACE). The <c>email_templates_name_key</c> collision maps to <c>duplicate-name</c>;
/// a delete blocked by a future dependent (RESTRICT backstop) maps to <c>dependency-exists</c>.
/// </remarks>
public sealed class EmailTemplateRepository : IEmailTemplateRepository
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the repository over the application persistence context.</summary>
    public EmailTemplateRepository(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<EmailTemplate?> GetByIdAsync(Guid emailTemplateId, CancellationToken cancellationToken)
    {
        var entity = await Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(template => template.EmailTemplateId == emailTemplateId, cancellationToken);

        return entity is null ? null : Project(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken cancellationToken)
    {
        // Deterministic technical order (name ASC, id tie-break); no ranking meaning.
        var entities = await Templates
            .AsNoTracking()
            .OrderBy(template => template.Name)
            .ThenBy(template => template.EmailTemplateId)
            .ToListAsync(cancellationToken);

        return entities.Select(Project).ToList();
    }

    /// <inheritdoc />
    public async Task<EmailTemplate> CreatedAsync(EmailTemplate template, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            Templates.Add(new EmailTemplateEntity
            {
                EmailTemplateId = template.EmailTemplateId.Value,
                Name = template.Name.Trim(),
                Subject = template.Subject.Trim(),
                Body = template.Body.Trim(),
                DocumentType = template.DocumentType is { } type
                    ? EmailTemplateDocumentTypeTokens.ToToken(type)
                    : null,
                // P2-T08 email slice group routing (machine → group → template → list).
                MachineGroup = template.MachineGroup is { } group
                    ? EmailMachineGroupTokens.ToToken(group)
                    : null,
                EmailListId = template.EmailListId?.Value,
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

        return await GetByIdAsync(template.EmailTemplateId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The created email template '{template.EmailTemplateId}' could not be read back after commit.");
    }

    /// <inheritdoc />
    public async Task<EmailTemplate> UpdatedAsync(EmailTemplate template, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await Templates
                .FirstOrDefaultAsync(candidate => candidate.EmailTemplateId == template.EmailTemplateId.Value, cancellationToken)
                ?? throw new ConcurrencyConflictException(
                    $"Email template '{template.EmailTemplateId}' no longer exists; nothing was written.");

            if (entity.Version != template.Version)
            {
                throw new ConcurrencyConflictException(
                    $"Email template '{template.EmailTemplateId}' was modified concurrently (expected version " +
                    $"{template.Version}, current version {entity.Version}); reload and retry.");
            }

            entity.Name = template.Name.Trim();
            entity.Subject = template.Subject.Trim();
            entity.Body = template.Body.Trim();
            entity.DocumentType = template.DocumentType is { } type
                ? EmailTemplateDocumentTypeTokens.ToToken(type)
                : null;
            // P2-T08 email slice group routing (machine → group → template → list).
            entity.MachineGroup = template.MachineGroup is { } group
                ? EmailMachineGroupTokens.ToToken(group)
                : null;
            entity.EmailListId = template.EmailListId?.Value;
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

        return await GetByIdAsync(template.EmailTemplateId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The updated email template '{template.EmailTemplateId}' could not be read back after commit.");
    }

    /// <inheritdoc />
    public async Task DeletedAsync(Guid emailTemplateId, int expectedVersion, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await Templates
                .FirstOrDefaultAsync(candidate => candidate.EmailTemplateId == emailTemplateId, cancellationToken)
                ?? throw new ConcurrencyConflictException(
                    $"Email template '{emailTemplateId}' no longer exists; nothing was deleted.");

            if (entity.Version != expectedVersion)
            {
                throw new ConcurrencyConflictException(
                    $"Email template '{emailTemplateId}' was modified concurrently (expected version " +
                    $"{expectedVersion}, current version {entity.Version}); nothing was deleted.");
            }

            Templates.Remove(entity);

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

    private DbSet<EmailTemplateEntity> Templates => _context.Set<EmailTemplateEntity>();

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
            case "23505": // unique_violation — the unique template name.
                if (string.Equals(
                        postgresException.ConstraintName,
                        EmailTemplateEntityConfiguration.NameUniqueConstraintName,
                        StringComparison.Ordinal))
                {
                    failure = new ControloPersistenceException(
                        ControloPersistenceFailureReason.DuplicateTemplateName,
                        "Another email template already owns this name.",
                        exception);
                    return true;
                }

                break;

            case "23503": // RESTRICT backstop against a future dependent.
                failure = new ControloPersistenceException(
                    ControloPersistenceFailureReason.DependencyExists,
                    "A dependent record still references this email template; nothing was deleted.",
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

    private static EmailTemplate Project(EmailTemplateEntity entity) => new(
        DomainEmailTemplateId.From(entity.EmailTemplateId),
        entity.Name,
        entity.Subject,
        entity.Body,
        EmailTemplateDocumentTypeTokens.Parse(entity.DocumentType),
        EmailMachineGroupTokens.Parse(entity.MachineGroup),
        entity.EmailListId is { } listId ? EmailListId.From(listId) : null,
        entity.Version,
        entity.CreatedAt,
        entity.UpdatedAt);
}