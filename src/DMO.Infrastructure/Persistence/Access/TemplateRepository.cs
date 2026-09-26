using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Application.Templates;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DMO.Infrastructure.Persistence.Access;

/// <summary>
/// <see cref="ITemplateRepository"/> implementation over the single application persistence
/// context.
/// </summary>
/// <remarks>
/// <para>
/// Template create and update persist the <c>template_modules</c> composition in the same
/// transaction as the <c>templates</c> row; the Template row <c>version</c> is the
/// concurrency boundary for the whole composition. P1-T06 owns Template administration
/// workflows; P1-T03 provides these primitives only.
/// </para>
/// </remarks>
public sealed class TemplateRepository : ITemplateRepository
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the repository over the application persistence context.</summary>
    public TemplateRepository(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<Template?> GetByIdAsync(Guid templateId, CancellationToken cancellationToken)
        => Project(_context.Templates.Where(template => template.TemplateId == templateId))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Template>> ListAsync(CancellationToken cancellationToken)
        => await Project(_context.Templates)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Guid> CreatedAsync(
        Template template,
        IReadOnlyList<TemplateModule> modules,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(modules);

        var templateId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        _context.Templates.Add(new TemplateEntity
        {
            TemplateId = templateId,
            Name = template.Name,
            LandingDestinationId = template.LandingDestinationId,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });

        AddComposition(templateId, modules, now);

        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return templateId;
    }

    /// <inheritdoc />
    public async Task UpdatedAsync(
        Template template,
        IReadOnlyList<TemplateModule> modules,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(modules);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var entity = await _context.Templates
            .SingleOrDefaultAsync(candidate => candidate.TemplateId == template.TemplateId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Template '{template.TemplateId}' was not found (deleted or invalid identifier).");

        if (entity.Version != template.Version)
        {
            throw new ConcurrencyConflictException(
                $"Template '{template.TemplateId}' was modified concurrently (expected version " +
                $"{template.Version}, current version {entity.Version}); reload and retry.");
        }

        // Every committed write bumps the composition's version: the row is the
        // concurrency boundary and the version is the observable revision number.
        entity.Version += 1;
        entity.Name = template.Name;
        entity.LandingDestinationId = template.LandingDestinationId;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await ReplaceComposition(template.TemplateId, modules);

        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid templateId, int expectedVersion, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var entity = await _context.Templates
            .SingleOrDefaultAsync(candidate => candidate.TemplateId == templateId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Template '{templateId}' was not found (deleted or invalid identifier).");

        if (entity.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                $"Template '{templateId}' was modified concurrently (expected version {expectedVersion}, " +
                $"current version {entity.Version}); reload and retry.");
        }

        _context.Templates.Remove(entity);

        // Referenced Templates cannot be deleted here: users.template_id uses ON DELETE
        // RESTRICT, so a referenced Template fails at the database layer (P1-T06 owns the
        // atomic null-out workflow).
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteWithMembersAsync(Guid templateId, int expectedVersion, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // 1. Version verified INSIDE the destructive transaction: a stale concurrent write is
        //    never erased — the whole sequence rolls back and nothing changes.
        var entity = await _context.Templates
            .SingleOrDefaultAsync(candidate => candidate.TemplateId == templateId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Template '{templateId}' was not found (deleted or invalid identifier).");

        if (entity.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                $"Template '{templateId}' was modified concurrently (expected version {expectedVersion}, " +
                $"current version {entity.Version}); reload and retry.");
        }

        // 2. Atomic null-out of every USER reference, INSIDE the same transaction and BEFORE
        //    the row delete. users.template_id is ON DELETE RESTRICT, so the references must
        //    be cleared first; this UPDATE covers every user currently assigned at the moment
        //    it runs (committed membership included), active or inactive. USER rows themselves
        //    are never cascade-deleted and keep their active state.
        await _context.Users
            .Where(user => user.TemplateId == templateId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(user => user.TemplateId, (Guid?)null),
                cancellationToken);

        // 3. Delete the row; template_modules is cascade-removed by the database.
        _context.Templates.Remove(entity);

        // 4. One commit for the null-out + delete (or a full rollback on any failure — e.g. a
        //    membership write committed between the null-out and this delete re-introduces a
        //    RESTRICT violation and the entire transaction rolls back: no half-state).
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private void AddComposition(Guid templateId, IReadOnlyList<TemplateModule> modules, DateTimeOffset now)
    {
        foreach (var module in modules)
        {
            _context.TemplateModules.Add(new TemplateModuleEntity
            {
                TemplateId = templateId,
                ModuleId = module.ModuleId,
                PresentationOrder = module.PresentationOrder,
            });
        }
    }

    private async Task ReplaceComposition(Guid templateId, IReadOnlyList<TemplateModule> modules)
    {
        var existing = await _context.TemplateModules
            .Where(module => module.TemplateId == templateId)
            .ToListAsync();

        _context.TemplateModules.RemoveRange(existing);

        foreach (var module in modules)
        {
            _context.TemplateModules.Add(new TemplateModuleEntity
            {
                TemplateId = templateId,
                ModuleId = module.ModuleId,
                PresentationOrder = module.PresentationOrder,
            });
        }
    }

    private static IQueryable<Template> Project(IQueryable<TemplateEntity> source) =>
        source.Select(template => new Template(
            template.TemplateId,
            template.Name,
            template.LandingDestinationId,
            template.Version));

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
}