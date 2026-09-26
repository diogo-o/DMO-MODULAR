using DMO.Application.JobOn;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DMO.Infrastructure.Persistence.ToolJobOn;

/// <summary>
/// The Job On module's own delete-dependency probe: the recorded duplication lineage.
/// </summary>
/// <remarks>
/// Authority: P2-T04 contract §11.5 and §12.4. A Job On used as the recorded duplication source is a
/// dependency, because its lineage is part of persisted history and must not be silently removed;
/// the self-referencing <c>RESTRICT</c> foreign key enforces the same rule at the database level.
/// <para>
/// This probe reports a <b>fact</b> only. Later workstreams (Peso, Pegamentos, Folha, Resumo,
/// Boquilhas, documents) register their own probe through one additive line each, so the delete flow
/// includes every registered module without P2-T04 knowing its domain.
/// </para>
/// </remarks>
public sealed class JobOnLineageDependencyProbe : IJobOnDependencyProbe
{
    /// <summary>The dependency kind reported by this probe.</summary>
    public const string DuplicationLineageKind = "duplication-lineage";

    /// <summary>The contributing-source name reported by this probe.</summary>
    public const string SourceName = "job-on";

    private readonly DmoDbContext _context;

    /// <summary>Creates the probe over the application persistence context.</summary>
    public JobOnLineageDependencyProbe(DmoDbContext context)
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

        var dependents = await _context.Set<JobOnEntity>()
            .AsNoTracking()
            .Where(jobOn => jobOn.CopiedFromJobOnId == target.JobOnId)
            .OrderBy(jobOn => jobOn.Reference)
            .ThenBy(jobOn => jobOn.ProductionNumber)
            .Select(jobOn => new { jobOn.Reference, jobOn.ProductionNumber })
            .ToListAsync(cancellationToken);

        if (dependents.Count == 0)
        {
            return JobOnDependencyReport.None(SourceName);
        }

        return new JobOnDependencyReport(
            SourceName,
            dependents
                .Select(dependent => new JobOnDependency(
                    DuplicationLineageKind,
                    $"O Job On '{dependent.Reference}' / produção '{dependent.ProductionNumber}' foi " +
                    "duplicado a partir deste Job On."))
                .ToList());
    }
}
