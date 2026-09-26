using DMO.Application.JobOn;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

using DMO.Infrastructure.Persistence;
namespace DMO.Infrastructure.Persistence.Controlo;

/// <summary>
/// The P2-T05 delete-dependency probe: Pesos that depend on a Job On occurrence.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §20.4.1. It reports <c>peso</c> (kind) with a human description for
/// every Peso whose <c>cm_id</c> is one of the target's context ids (a Peso's production anchor is
/// a real <c>cm_contexts</c> row, so the CM context id is the scoped relation), <b>or</b> whose
/// pending <c>tool_id</c> is the Tool behind the target's CM context when the target is a
/// context-bearing Job On. Thus a Job On delete (and a CM removal inside a Job On edit) refuses
/// with <c>dependency-exists</c> naming <c>peso</c> before the RESTRICT FK backstop. The probe
/// reports a <b>fact</b> only; no dependency table is introduced (P2-T04 §11.5).
/// </remarks>
public sealed class PesoJobOnDependencyProbe : IJobOnDependencyProbe
{
    /// <summary>The dependency kind reported by this probe.</summary>
    public const string PesoDependencyKind = "peso";

    /// <summary>The contributing-source name reported by this probe.</summary>
    public const string SourceName = "controlo-create";

    private readonly DmoDbContext _context;

    /// <summary>Creates the probe over the application persistence context.</summary>
    public PesoJobOnDependencyProbe(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<JobOnDependencyReport> InspectAsync(
        JobOnDependencyTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var contextIds = new[] { target.CmContextId, target.MfContextId, target.BqContextId }
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

        // A Peso is anchored either to a real cm_contexts row of this occurrence, or truthfully to
        // a canonical Tool (pending). The pending case depends on the occurrence only when the
        // occurrence's CM context resolves to that same Tool (context-bearing target).
        Guid? cmToolId = null;
        if (target.CmContextId is { } cmId)
        {
            cmToolId = await _context.Set<CmContextEntity>()
                .AsNoTracking()
                .Where(context => context.CmId == cmId)
                .Select(context => (Guid?)context.ToolId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var dependents = new List<JobOnDependency>();

        if (contextIds.Count > 0)
        {
            var productionBound = await _context.Set<PesoEntity>()
                .AsNoTracking()
                .Where(peso => peso.CmId != null && contextIds.Contains(peso.CmId!.Value))
                .Select(peso => new { peso.PesoId })
                .OrderBy(peso => peso.PesoId)
                .ToListAsync(cancellationToken);

            dependents.AddRange(productionBound.Select(peso => new JobOnDependency(
                PesoDependencyKind,
                $"O Peso '{peso.PesoId}' está associado ao contexto CM desta produção.")));
        }

        if (cmToolId is { } toolId)
        {
            var pending = await _context.Set<PesoEntity>()
                .AsNoTracking()
                .Where(peso => peso.ToolId == toolId && peso.CmId == null)
                .Select(peso => new { peso.PesoId })
                .OrderBy(peso => peso.PesoId)
                .ToListAsync(cancellationToken);

            dependents.AddRange(pending.Select(peso => new JobOnDependency(
                PesoDependencyKind,
                $"O Peso '{peso.PesoId}' está por associar à ferramenta CM desta produção.")));
        }

        return dependents.Count == 0
            ? JobOnDependencyReport.None(SourceName)
            : new JobOnDependencyReport(SourceName, dependents);
    }
}