using DMO.Application.Controlo.Pesos;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

using DMO.Infrastructure.Persistence;
namespace DMO.Infrastructure.Persistence.Controlo;

/// <summary>
/// <see cref="IPesoContextRead"/> over the single application persistence context.
/// </summary>
/// <remarks>
/// Read-only resolution of the contracted P2-T05 anchor traversal <c>cm_id → tool_id → jobon_id</c>
/// (§6.1/§7.2/§26.3): a single-table <c>SELECT</c> over <c>cm_contexts</c>, no tracking, no write,
/// no version bump, no creation (P2-T04 read-transaction rule). It never recreates a CM context and
/// never mints any identity — it only reads the real P2-T04 row behind a supplied <c>cm_id</c>.
/// </remarks>
public sealed class DmoPesoContextRead : IPesoContextRead
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the read over the application persistence context.</summary>
    public DmoPesoContextRead(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<CmContextProjection?> GetCmContextAsync(
        Guid cmId,
        CancellationToken cancellationToken)
    {
        var row = await _context.Set<CmContextEntity>()
            .AsNoTracking()
            .Where(context => context.CmId == cmId)
            .Select(context => new CmContextProjection(
                context.CmId,
                context.JobOnId,
                context.ToolId,
                context.ToolType,
                context.ToolReference,
                context.ToolLot))
            .FirstOrDefaultAsync(cancellationToken);

        return row?.CmId == Guid.Empty ? null : row;
    }
}