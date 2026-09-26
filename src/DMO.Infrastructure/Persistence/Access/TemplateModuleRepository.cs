using DMO.Application.Repositories;
using DMO.Application.Templates;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DMO.Infrastructure.Persistence.Access;

/// <summary>
/// <see cref="ITemplateModuleRepository"/> implementation over the single application
/// persistence context.
/// </summary>
/// <remarks>
/// Reads only: the composition is owned by <see cref="ITemplateRepository"/> write
/// transactions. <see cref="TemplateModule.ModuleId"/> values are stable code-defined
/// identities; there is no Module-definition table.
/// </remarks>
public sealed class TemplateModuleRepository : ITemplateModuleRepository
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the repository over the application persistence context.</summary>
    public TemplateModuleRepository(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateModule>> GetByTemplateAsync(
        Guid templateId,
        CancellationToken cancellationToken)
        => await _context.TemplateModules
            .Where(module => module.TemplateId == templateId)
            .OrderBy(module => module.PresentationOrder)
            .Select(module => new TemplateModule(
                module.TemplateId,
                module.ModuleId,
                module.PresentationOrder))
            .ToListAsync(cancellationToken);
}