using DMO.Application.ControloCreate;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

using DMO.Infrastructure.Persistence;
namespace DMO.Infrastructure.Persistence.Controlo;

/// <summary>
/// <see cref="IPesoOutputRead"/> over the single application persistence context.
/// </summary>
/// <remarks>
/// ONE context-specific statement resolves the relation <c>jobon_id → cm_contexts → pesos</c> (the
/// P2-T05 anchor traversal read backwards): a Peso is an output of the occurrence exactly when its
/// <c>cm_id</c> is a real <c>cm_contexts</c> row of that occurrence. Only the <c>peso_id</c> values
/// are selected (light-packet rule), only production-bound Pesos are ever considered (a pending
/// <c>Job On por associar</c> Peso anchors on <c>tool_id</c> and has no document target), and the
/// read is read-only: no tracking, no write, no version bump, no creation (P2-T04 read-transaction
/// rule).
/// </remarks>
public sealed class DmoPesoOutputRead : IPesoOutputRead
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the read over the application persistence context.</summary>
    public DmoPesoOutputRead(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListByJobOnIdAsync(
        Guid jobOnId,
        CancellationToken cancellationToken)
    {
        // The INNER JOIN expresses the real anchor: only Pesos whose cm_id IS a cm_contexts row of
        // this occurrence are outputs of it — never a heuristic and never a stored relation.
        var pesos = await (
            from peso in _context.Set<PesoEntity>().AsNoTracking()
            join cm in _context.Set<CmContextEntity>().AsNoTracking()
                on peso.CmId equals cm.CmId
            where cm.JobOnId == jobOnId
            orderby peso.CreatedAt, peso.PesoId
            select peso.PesoId)
            .ToListAsync(cancellationToken);

        return pesos;
    }
}