using DMO.Application.JobOn;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

using DMO.Infrastructure.Persistence;
namespace DMO.Infrastructure.Persistence.Boquilhas;

/// <summary>
/// The P2-T07 delete-dependency probe: Boquilhas aggregates that depend on a Job On occurrence.
/// </summary>
/// <remarks>
/// Authority: P2-T07 contract §27 ("P2-T07 registers
/// <c>BoquilhasDependencyProbe : IJobOnDependencyProbe</c> — one additive line in <c>Program.cs</c>,
/// accepted seam: reports <c>boquilhas-aggregate</c> dependencies when aggregates reference the
/// target <c>bq_id</c>/Job On — app-level <c>dependency-exists</c> before the DB backstop").
/// <para>
/// A production-linked aggregate anchors a real <c>bq_contexts</c> row of the target, so the
/// scoped relation is <c>boquilhas.bq_id</c> ∈ the target's context ids; a standalone aggregate has
/// no Job On relation at all and never depends on the target (the standalone anchor protection is
/// the <c>tools</c> RESTRICT FK, §27). The probe reports a <b>fact</b> only; no dependency table is
/// introduced (P2-T04 §11.5).</para>
/// </remarks>
public sealed class BoquilhasDependencyProbe : IJobOnDependencyProbe
{
    /// <summary>The dependency kind reported by this probe.</summary>
    public const string BoquilhasDependencyKind = "boquilhas-aggregate";

    /// <summary>The contributing-source name reported by this probe.</summary>
    public const string SourceName = "boquilhas";

    private readonly DmoDbContext _context;

    /// <summary>Creates the probe over the application persistence context.</summary>
    public BoquilhasDependencyProbe(DmoDbContext context)
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

        var bqContextIds = new[] { target.CmContextId, target.MfContextId, target.BqContextId }
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

        // A Boquilhas register is production-linked exactly when it anchors a real bq_contexts row
        // of the target occurrence (the ONLY settled anchor; the transitional §34 pré-JobOn
        // tool_id anchor has no Job On relation and never depends on the target — the standalone
        // protection is the tools RESTRICT FK; a pending register is protected by nothing Job-On
        // related).
        if (bqContextIds.Count == 0)
        {
            return JobOnDependencyReport.None(SourceName);
        }

        var dependents = await _context.Set<BoquilhaEntity>()
            .AsNoTracking()
            .Where(register => register.BqId != null && bqContextIds.Contains(register.BqId!.Value))
            .Select(register => new { register.BoquilhasId })
            .OrderBy(register => register.BoquilhasId)
            .ToListAsync(cancellationToken);

        return dependents.Count == 0
            ? JobOnDependencyReport.None(SourceName)
            : new JobOnDependencyReport(
                SourceName,
                dependents.Select(aggregate => new JobOnDependency(
                    BoquilhasDependencyKind,
                    $"O registo de Boquilhas '{aggregate.BoquilhasId}' está ancorado ao contexto BQ desta produção."))
                .ToList());
    }
}