using DMO.Application.Controlo.Pesos;
using DMO.Application.Tools;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

using DMO.Infrastructure.Persistence;
namespace DMO.Infrastructure.Persistence.Controlo;

/// <summary>
/// <see cref="IProductionResumoRead"/> over the single application persistence context.
/// </summary>
/// <remarks>
/// The Resumo da produção is resolved by ONE context-specific statement (the OWNER CLARIFICATION
/// surgical-queries / light-packets rule): <c>job_ons</c> LEFT JOIN <c>cm_contexts</c> LEFT JOIN
/// <c>tools</c>, keyed by the real <c>jobon_id</c>, selecting exactly the entry-surface columns
/// (no <c>tool_machines</c> rows, no context of any other slot, no output/history rows). The
/// projection is read-only: no tracking, no write, no version bump, no creation (P2-T04
/// read-transaction rule) and no second production authority — the Job On remains the only written
/// source of these facts. An anonymous row carrier is materialized inside the statement and mapped
/// in memory, because the <c>Processo</c> token parsing is deliberately not a translatable
/// expression (accepted <c>LoadContextsAsync</c> pattern).
/// </remarks>
public sealed class DmoProductionResumoRead : IProductionResumoRead
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the read over the application persistence context.</summary>
    public DmoProductionResumoRead(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<ProductionResumoReadModel?> GetResumoAsync(
        Guid jobOnId,
        CancellationToken cancellationToken)
    {
        // ONE parameterized statement: select only the entry-surface columns of the occurrence,
        // its CM context (0..1 rows by cm_contexts_jobon_key) and the live Tool facts of that
        // context. The two LEFT JOINs keep a Job On without a CM context a valid Resumo.
        var row = await (
            from jobOn in _context.Set<JobOnEntity>().AsNoTracking()
            join cm in _context.Set<CmContextEntity>().AsNoTracking()
                on jobOn.JobOnId equals cm.JobOnId into cmGroup
            from cm in cmGroup.DefaultIfEmpty()
            join tool in _context.Set<ToolEntity>().AsNoTracking()
                on cm!.ToolId equals tool.ToolId into toolGroup
            from tool in toolGroup.DefaultIfEmpty()
            where jobOn.JobOnId == jobOnId
            select new ResumoRow(
                jobOn.JobOnId,
                jobOn.Reference,
                jobOn.ProductionNumber,
                jobOn.Machine,
                jobOn.ProductionDate,
                jobOn.Version,
                cm != null ? cm.CmId : (Guid?)null,
                cm != null ? cm.ToolId : (Guid?)null,
                cm != null ? cm.ToolType : null,
                cm != null ? cm.ToolReference : null,
                cm != null ? cm.ToolLot : null,
                tool != null ? tool.Reference : null,
                tool != null ? tool.Lot : null,
                tool != null ? tool.Processo : null,
                tool != null ? tool.Quantity : (int?)null))
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new ProductionResumoReadModel(
            row.JobOnId,
            row.Reference,
            row.ProductionNumber,
            row.Machine,
            row.ProductionDate,
            row.Version,
            row.CmId is { } cmId
                ? new CmResumoProjection(
                    cmId,
                    row.ToolId!.Value,
                    row.FrozenToolType!,
                    row.FrozenToolReference!,
                    row.FrozenToolLot!,
                    row.LiveToolReference ?? string.Empty,
                    row.LiveToolLot ?? string.Empty,
                    ToolTokens.ParseProcesso(row.LiveProcesso),
                    row.Quantity)
                : null);
    }

    /// <summary>
    /// The column carrier of the single statement. Every value is a direct row column; the
    /// nullable conditional projection expresses the outer-join null propagation.
    /// </summary>
    private sealed record ResumoRow(
        Guid JobOnId,
        string Reference,
        string ProductionNumber,
        string Machine,
        DateOnly? ProductionDate,
        int Version,
        Guid? CmId,
        Guid? ToolId,
        string? FrozenToolType,
        string? FrozenToolReference,
        string? FrozenToolLot,
        string? LiveToolReference,
        string? LiveToolLot,
        string? LiveProcesso,
        int? Quantity);
}