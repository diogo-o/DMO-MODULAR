using DMO.Application.Boquilhas;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

using DMO.Infrastructure.Persistence;
namespace DMO.Infrastructure.Persistence.Boquilhas;

/// <summary>
/// <see cref="IBoquilhasContextRead"/> implementation: the narrow read-only <c>bq_contexts</c>
/// traversal seam.
/// </summary>
/// <remarks>
/// Authority: P2-T07 contract §15.3/§22.4/§24 + the P2-T05 <c>DmoPesoContextRead</c> precedent
/// (review ACCEPT 4d88dbe): a single-table SELECT that resolves <c>bq_id → jobon_id + tool_id +
/// frozen triple</c> — the one chain the closed P2-T04 application contracts cannot resolve from a
/// bare <c>bq_id</c>. It makes no write and mints no identity; every other Job On fact comes from
/// <c>IJobOnService</c>.</remarks>
public sealed class DmoBoquilhasContextRead : IBoquilhasContextRead
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the read over the application persistence context.</summary>
    public DmoBoquilhasContextRead(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<BqContextRead?> GetBqContextAsync(Guid bqId, CancellationToken cancellationToken)
    {
        var context = await _context.Set<BqContextEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.BqId == bqId, cancellationToken);

        if (context is null)
        {
            return null;
        }

        return new BqContextRead(
            context.BqId,
            context.JobOnId,
            context.ToolId,
            context.ToolType,
            context.ToolReference,
            context.ToolLot);
    }
}