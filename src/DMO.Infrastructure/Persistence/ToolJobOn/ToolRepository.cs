using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Tools;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Entities;
using DMO.Infrastructure.Persistence.EntityConfigurations;
using Microsoft.EntityFrameworkCore;

namespace DMO.Infrastructure.Persistence.ToolJobOn;

/// <summary>
/// <see cref="IToolRepository"/> implementation over the single application persistence context.
/// </summary>
/// <remarks>
/// <para>
/// The repository obtains its sets with <c>_context.Set&lt;TEntity&gt;()</c>: EF discovers the new
/// configurations through <c>ApplyConfigurationsFromAssembly</c>, so <c>DmoDbContext</c> stays
/// untouched.
/// </para>
/// <para>
/// Tool create writes the Tool and its full compatibility set in one transaction; a losing
/// concurrent insert is mapped from <c>23505</c> + the constraint name onto the typed
/// <see cref="ToolPersistenceException"/>. There is no Tool update and no Tool delete primitive.
/// </para>
/// </remarks>
public sealed class ToolRepository : IToolRepository
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the repository over the application persistence context.</summary>
    public ToolRepository(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<Tool?> GetByIdAsync(Guid toolId, CancellationToken cancellationToken)
    {
        var entity = await Tools
            .AsNoTracking()
            .Include(tool => tool.Machines)
            .FirstOrDefaultAsync(tool => tool.ToolId == toolId, cancellationToken);

        return entity is null ? null : Project(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Tool>> SearchAsync(
        ToolSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var query = Tools.AsNoTracking();

        if (criteria.Type is { } type)
        {
            var token = ToolTokens.ToToken(type);
            query = query.Where(tool => tool.ToolType == token);
        }

        if (criteria.Reference is { } reference)
        {
            query = query.Where(tool => tool.Reference == reference);
        }

        if (criteria.Lot is { } lot)
        {
            query = query.Where(tool => tool.Lot == lot);
        }

        if (criteria.Query is { } text)
        {
            // Case-insensitive substring match over reference and lot ONLY, with the search text
            // escaped so a '%' or '_' typed by the operator is a literal character and never a
            // wildcard that silently widens the result.
            var pattern = LikePattern(text);
            query = query.Where(tool =>
                EF.Functions.ILike(tool.Reference, pattern, LikeEscape) ||
                EF.Functions.ILike(tool.Lot, pattern, LikeEscape));
        }

        if (criteria.Machine is { } machine)
        {
            // "Registered as compatible with that machine", read from tool_machines: never "the Tool
            // is physically there".
            var machineValue = machine.Value;
            query = query.Where(tool => tool.Machines.Any(row => row.Machine == machineValue));
        }

        // The limit is applied at the database level, to TOOLS (not to joined rows), so the number
        // of returned items never exceeds the limit even when a Tool has several machines.
        var toolIds = await query
            .OrderBy(tool => tool.ToolType)
            .ThenBy(tool => tool.Reference)
            .ThenBy(tool => tool.Lot)
            .ThenBy(tool => tool.ToolId)
            .Take(criteria.Limit)
            .Select(tool => tool.ToolId)
            .ToListAsync(cancellationToken);

        if (toolIds.Count == 0)
        {
            return [];
        }

        var entities = await Tools
            .AsNoTracking()
            .Include(tool => tool.Machines)
            .Where(tool => toolIds.Contains(tool.ToolId))
            .ToListAsync(cancellationToken);

        // The contracted deterministic technical order: tool_type, reference, lot, tool_id. It
        // carries no industrial meaning and no relevance ranking.
        return entities
            .OrderBy(tool => tool.ToolType, StringComparer.Ordinal)
            .ThenBy(tool => tool.Reference, StringComparer.Ordinal)
            .ThenBy(tool => tool.Lot, StringComparer.Ordinal)
            .ThenBy(tool => tool.ToolId)
            .Select(Project)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<Tool?> FindByIdentityAsync(
        ToolType type,
        string reference,
        string lot,
        CancellationToken cancellationToken)
    {
        var token = ToolTokens.ToToken(type);

        var entity = await Tools
            .AsNoTracking()
            .Include(tool => tool.Machines)
            .FirstOrDefaultAsync(
                tool => tool.ToolType == token && tool.Reference == reference && tool.Lot == lot,
                cancellationToken);

        return entity is null ? null : Project(entity);
    }

    /// <inheritdoc />
    public async Task<Tool> CreatedAsync(
        Tool tool,
        IReadOnlyList<MachineCode> compatibleMachines,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(compatibleMachines);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Identity query repeated INSIDE the transaction: the useful operator message names the
        // winning canonical tool_id, and the unique index closes the remaining race below.
        var existingId = await FindIdentityIdAsync(tool.Type, tool.Reference, tool.Lot, cancellationToken);
        if (existingId is { } conflictId)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ToolPersistenceException(
                ToolPersistenceFailureReason.DuplicateIdentity,
                DuplicateMessage(tool.Reference, tool.Lot),
                existingToolId: conflictId);
        }

        var entity = new ToolEntity
        {
            ToolId = tool.ToolId.Value,
            ToolType = ToolTokens.ToToken(tool.Type),
            Reference = tool.Reference,
            Lot = tool.Lot,
            Processo = ToolTokens.ToToken(tool.Processo),
            Quantity = tool.Quantity,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _context.Set<ToolEntity>().Add(entity);

        foreach (var machine in compatibleMachines)
        {
            _context.Set<ToolMachineEntity>().Add(new ToolMachineEntity
            {
                ToolMachineId = Guid.NewGuid(),
                ToolId = entity.ToolId,
                Machine = machine.Value,
            });
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryMapUniqueViolation(exception, out var winnerId))
        {
            // The transaction is aborted: roll it back before reading the winner, so the read runs
            // on a usable connection.
            await SafeRollbackAsync(transaction, cancellationToken);

            var winner = winnerId
                ?? await FindIdentityIdAsync(tool.Type, tool.Reference, tool.Lot, cancellationToken);

            throw new ToolPersistenceException(
                ToolPersistenceFailureReason.DuplicateIdentity,
                DuplicateMessage(tool.Reference, tool.Lot),
                exception,
                winner);
        }

        // The frozen compatibility set is read back from the persisted rows, so the returned Tool is
        // exactly what a later read returns.
        return await GetByIdAsync(entity.ToolId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The created Tool '{entity.ToolId}' could not be read back after commit.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ToolUsageOccurrence>> ListUsageOccurrencesAsync(
        Guid toolId,
        CancellationToken cancellationToken)
    {
        // Reverse navigation is a QUERY through the context tables, never an array on the Tool.
        var cm = await _context.Set<CmContextEntity>()
            .AsNoTracking()
            .Where(context => context.ToolId == toolId)
            .Join(
                _context.Set<JobOnEntity>().AsNoTracking(),
                context => context.JobOnId,
                jobOn => jobOn.JobOnId,
                (_, jobOn) => new UsageRow(
                    jobOn.JobOnId, jobOn.Reference, jobOn.ProductionNumber, jobOn.Machine))
            .ToListAsync(cancellationToken);

        var mf = await _context.Set<MfContextEntity>()
            .AsNoTracking()
            .Where(context => context.ToolId == toolId)
            .Join(
                _context.Set<JobOnEntity>().AsNoTracking(),
                context => context.JobOnId,
                jobOn => jobOn.JobOnId,
                (_, jobOn) => new UsageRow(
                    jobOn.JobOnId, jobOn.Reference, jobOn.ProductionNumber, jobOn.Machine))
            .ToListAsync(cancellationToken);

        var bq = await _context.Set<BqContextEntity>()
            .AsNoTracking()
            .Where(context => context.ToolId == toolId)
            .Join(
                _context.Set<JobOnEntity>().AsNoTracking(),
                context => context.JobOnId,
                jobOn => jobOn.JobOnId,
                (_, jobOn) => new UsageRow(
                    jobOn.JobOnId, jobOn.Reference, jobOn.ProductionNumber, jobOn.Machine))
            .ToListAsync(cancellationToken);

        return cm.Concat(mf).Concat(bq)
            .Distinct()
            .OrderBy(row => row.Reference, StringComparer.Ordinal)
            .ThenBy(row => row.ProductionNumber, StringComparer.Ordinal)
            .ThenBy(row => row.JobOnId)
            .Select(row => new ToolUsageOccurrence(
                row.JobOnId, row.Reference, row.ProductionNumber, row.Machine))
            .ToList();
    }

    private const string LikeEscape = "\\";

    private IQueryable<ToolEntity> Tools => _context.Set<ToolEntity>();

    private static string LikePattern(string value) =>
        "%" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";

    private Task<Guid?> FindIdentityIdAsync(
        ToolType type,
        string reference,
        string lot,
        CancellationToken cancellationToken)
    {
        var token = ToolTokens.ToToken(type);

        return Tools
            .AsNoTracking()
            .Where(tool => tool.ToolType == token && tool.Reference == reference && tool.Lot == lot)
            .Select(tool => (Guid?)tool.ToolId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool TryMapUniqueViolation(DbUpdateException exception, out Guid? existingToolId)
    {
        existingToolId = null;

        var postgresException = exception.InnerException as Npgsql.PostgresException
            ?? exception.InnerException?.InnerException as Npgsql.PostgresException;

        return postgresException is { SqlState: "23505" } &&
            string.Equals(
                postgresException.ConstraintName,
                ToolEntityConfiguration.TypeReferenceLotUniqueConstraintName,
                StringComparison.Ordinal);
    }

    private static async Task SafeRollbackAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Already completed/rolled back by the provider: nothing to undo.
        }
        catch (Npgsql.PostgresException)
        {
            // The connection reported the failed transaction state: the rollback is already implied.
        }
    }

    private static string DuplicateMessage(string reference, string lot) =>
        $"A canonical Tool with this type, reference '{reference}' and lot '{lot}' already exists; " +
        "select the existing Tool instead of creating a duplicate.";

    private static Tool Project(ToolEntity entity)
    {
        var type = ToolTokens.ParseType(entity.ToolType)
            ?? throw new InvalidOperationException(
                $"Persisted tool_type '{entity.ToolType}' is not one of CM/MF/BQ.");

        return new Tool(
            ToolId.From(entity.ToolId),
            type,
            entity.Reference,
            entity.Lot,
            ToolTokens.ParseProcesso(entity.Processo),
            entity.Quantity,
            entity.Machines
                .Select(machine => MachineCode.From(machine.Machine))
                .OrderBy(machine => machine.Order)
                .ToList());
    }

    private sealed record UsageRow(Guid JobOnId, string Reference, string ProductionNumber, string Machine);
}
