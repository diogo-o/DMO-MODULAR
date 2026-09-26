using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;
using DMO.Infrastructure.Persistence.Entities;
using DMO.Infrastructure.Persistence.EntityConfigurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using DomainPesoId = DMO.Domain.Controlo.PesoId;

using DMO.Infrastructure.Persistence;
namespace DMO.Infrastructure.Persistence.Controlo;

/// <summary>
/// <see cref="IPesoRepository"/> implementation over the single application persistence context.
/// </summary>
/// <remarks>
/// <para>
/// Every write opens its own transaction: the Peso row plus its full measurement-row set are one
/// unit, and a failure at any step leaves neither a partial Peso nor a half-written row set
/// (atomicity — CRE1/AC-C1; the forced mid-transaction failure leaves zero rows). The anchor
/// existence is pre-resolved by the service and re-guarded here by the RESTRICT foreign keys —
/// a vanished <c>cm_id</c>/<c>tool_id</c> surfaces as the typed
/// <c>CM_CONTEXT_NOT_FOUND</c>/<c>TOOL_NOT_FOUND</c> refusal, never as a generic 500.
/// </para>
/// <para>
/// The <c>capacity_cm3 &gt; 0</c>/<c>glass_weight_g &gt; 0</c> CHECKs are the mapped backstop:
/// SQLSTATE <c>23514</c> on <c>peso_measurement_rows_capacity_check</c> /
/// <c>peso_measurement_rows_glass_check</c> produces the same <c>ResultNonPositive</c> refusal the
/// validator raises (C2, §20.2) — the CHECKs are never weakened or removed. The <c>version</c>
/// token stays active through the accepted <see cref="SaveAsync"/> pattern (compare + EF-token
/// race → <c>ConcurrencyConflictException</c> → 409 <c>stale-version</c>).
/// </para>
/// </remarks>
public sealed class PesoRepository : IPesoRepository
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the repository over the application persistence context.</summary>
    public PesoRepository(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<Peso?> GetByIdAsync(Guid pesoId, CancellationToken cancellationToken)
    {
        var entity = await Pesos
            .AsNoTracking()
            .FirstOrDefaultAsync(peso => peso.PesoId == pesoId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var rows = await Rows
            .AsNoTracking()
            .Where(row => row.PesoId == pesoId)
            .OrderBy(row => row.RowPosition)
            .ToListAsync(cancellationToken);

        return Project(entity, rows);
    }

    /// <inheritdoc />
    public async Task<Peso> CreatedAsync(
        Peso peso,
        IReadOnlyList<PesoMeasurementRow> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(peso);
        ArgumentNullException.ThrowIfNull(rows);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            Pesos.Add(new PesoEntity
            {
                PesoId = peso.PesoId.Value,
                CmId = peso.CmId,
                ToolId = peso.ToolId,
                Status = PesoStatusTokens.ToToken(peso.Status),
                SubmittedAt = null,
                SubmittedByUserId = null,
                WaterTemperature = peso.WaterTemperature,
                VolumeMarisaBq = peso.VolumeMarisaBq,
                VolumePuncaoPu = peso.VolumePuncaoPu,
                GlassDensityGCm3 = peso.GlassDensityGCm3,
                PreviousProductionEndReference = TrimToNull(peso.PreviousProductionEndReference),
                PreviousAverageWeightReference = TrimToNull(peso.PreviousAverageWeightReference),
                Version = 1,
                CreatedByUserId = peso.CreatedByUserId,
                CreatedAt = now,
                UpdatedAt = now,
            });

            foreach (var row in rows)
            {
                InsertRow(peso.PesoId.Value, row, now);
            }

            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryMapWriteFailure(exception, out var failure))
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            _context.ChangeTracker.Clear();
            throw failure;
        }

        return await GetByIdAsync(peso.PesoId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The created Peso '{peso.PesoId}' could not be read back after commit.");
    }

    /// <inheritdoc />
    public async Task<Peso> UpdatedAsync(
        Peso peso,
        IReadOnlyList<PesoMeasurementRow> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(peso);
        ArgumentNullException.ThrowIfNull(rows);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await LoadTrackedAsync(peso.PesoId.Value, cancellationToken)
                ?? throw new ConcurrencyConflictException(
                    $"Peso '{peso.PesoId}' no longer exists (deleted concurrently); nothing was written.");

            AssertVersion(entity, peso.PesoId.Value, peso.Version);

            // The anchor is NOT editable through the edit command: only the fact columns change.
            entity.WaterTemperature = peso.WaterTemperature;
            entity.VolumeMarisaBq = peso.VolumeMarisaBq;
            entity.VolumePuncaoPu = peso.VolumePuncaoPu;
            entity.GlassDensityGCm3 = peso.GlassDensityGCm3;
            entity.PreviousProductionEndReference = TrimToNull(peso.PreviousProductionEndReference);
            entity.PreviousAverageWeightReference = TrimToNull(peso.PreviousAverageWeightReference);

            // Whole-set row replacement: the measurement row set is an aggregate part of the Peso.
            var existing = await Rows
                .Where(row => row.PesoId == peso.PesoId.Value)
                .ToListAsync(cancellationToken);
            if (existing.Count > 0)
            {
                Rows.RemoveRange(existing);
            }

            foreach (var row in rows)
            {
                InsertRow(peso.PesoId.Value, row, now);
            }

            entity.Version += 1;
            entity.UpdatedAt = now;

            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryMapWriteFailure(exception, out var failure))
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            _context.ChangeTracker.Clear();
            throw failure;
        }

        return await GetByIdAsync(peso.PesoId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The updated Peso '{peso.PesoId}' could not be read back after commit.");
    }

    /// <inheritdoc />
    public async Task<Peso> SubmittedAsync(Peso peso, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(peso);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await LoadTrackedAsync(peso.PesoId.Value, cancellationToken)
                ?? throw new ConcurrencyConflictException(
                    $"Peso '{peso.PesoId}' no longer exists (deleted concurrently); nothing was written.");

            AssertVersion(entity, peso.PesoId.Value, peso.Version);

            entity.SubmittedAt = peso.SubmittedAt ?? now;
            entity.SubmittedByUserId = peso.SubmittedByUserId;
            entity.Version += 1;
            entity.UpdatedAt = now;

            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryMapWriteFailure(exception, out var failure))
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            _context.ChangeTracker.Clear();
            throw failure;
        }

        return await GetByIdAsync(peso.PesoId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The submitted Peso '{peso.PesoId}' could not be read back after commit.");
    }

    /// <inheritdoc />
    public async Task<Peso> AssociatedAsync(Peso peso, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(peso);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await LoadTrackedAsync(peso.PesoId.Value, cancellationToken)
                ?? throw new ConcurrencyConflictException(
                    $"Peso '{peso.PesoId}' no longer exists (deleted concurrently); nothing was written.");

            AssertVersion(entity, peso.PesoId.Value, peso.Version);

            // The anchor swap: cm_id set, the temporary direct Tool anchor cleared (§4.4 rule 4).
            entity.CmId = peso.CmId;
            entity.ToolId = null;
            entity.Version += 1;
            entity.UpdatedAt = now;

            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryMapWriteFailure(exception, out var failure))
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            _context.ChangeTracker.Clear();
            throw failure;
        }

        return await GetByIdAsync(peso.PesoId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The associated Peso '{peso.PesoId}' could not be read back after commit.");
    }

    private DbSet<PesoEntity> Pesos => _context.Set<PesoEntity>();

    private DbSet<PesoMeasurementRowEntity> Rows => _context.Set<PesoMeasurementRowEntity>();

    private async Task<PesoEntity?> LoadTrackedAsync(Guid pesoId, CancellationToken cancellationToken) =>
        await Pesos.FirstOrDefaultAsync(peso => peso.PesoId == pesoId, cancellationToken);

    private static void AssertVersion(PesoEntity entity, Guid pesoId, int expectedVersion)
    {
        if (entity.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                $"Peso '{pesoId}' was modified concurrently (expected version {expectedVersion}, " +
                $"current version {entity.Version}); reload and retry.");
        }
    }

    private void InsertRow(Guid pesoId, PesoMeasurementRow row, DateTimeOffset now)
    {
        Rows.Add(new PesoMeasurementRowEntity
        {
            PesoMeasurementRowId = row.PesoMeasurementRowId.Value,
            PesoId = pesoId,
            RowPosition = row.RowPosition,
            WaterWeightG = row.WaterWeightG,
            CapacityCm3 = row.CapacityCm3,
            GlassWeightG = row.GlassWeightG,
            CreatedAt = now,
        });
    }

    /// <summary>
    /// Saves the tracked changes, mapping a save-time optimistic-concurrency conflict onto the
    /// domain typed conflict (contract §19.1): the explicit in-transaction version compare closes
    /// already-stale requests, while the active <c>IsConcurrencyToken</c> guard closes the remaining
    /// race between that compare and this save (accepted <c>SaveAsync</c> pattern, P2-T04 §15.1).
    /// </summary>
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

    /// <summary>
    /// Maps PostgreSQL constraint violations onto the typed application failures; everything else
    /// propagates unchanged (§20.2 binding rules).
    /// </summary>
    private static bool TryMapWriteFailure(DbUpdateException exception, out ControloPersistenceException failure)
    {
        failure = null!;

        var postgresException = exception.InnerException as Npgsql.PostgresException
            ?? exception.InnerException?.InnerException as Npgsql.PostgresException;

        if (postgresException is null)
        {
            return false;
        }

        switch (postgresException.SqlState)
        {
            case "23503": // foreign_key_violation — every P2-T05 FK is RESTRICT.
                if (string.Equals(
                        postgresException.ConstraintName,
                        PesoEntityConfiguration.CmForeignKeyConstraintName,
                        StringComparison.Ordinal))
                {
                    failure = new ControloPersistenceException(
                        ControloPersistenceFailureReason.CmContextNotFound,
                        "The supplied production anchor cm_id does not exist (or was removed concurrently).");
                    return true;
                }

                if (string.Equals(
                        postgresException.ConstraintName,
                        PesoEntityConfiguration.ToolForeignKeyConstraintName,
                        StringComparison.Ordinal))
                {
                    failure = new ControloPersistenceException(
                        ControloPersistenceFailureReason.ToolNotFound,
                        "The supplied pending anchor tool_id does not exist (or was removed concurrently).");
                    return true;
                }

                // The actor FKs (users) fail closed: a backend-set actor that vanished is a server
                // consistency failure, never a silent fallback.
                failure = new ControloPersistenceException(
                    ControloPersistenceFailureReason.DependencyExists,
                    "A referenced record still depends on this write target; nothing was written.");
                return true;

            case "23514": // check_violation — the C2 backstop.
                if (string.Equals(
                        postgresException.ConstraintName,
                        PesoMeasurementRowEntityConfiguration.CapacityCheckConstraintName,
                        StringComparison.Ordinal)
                    || string.Equals(
                        postgresException.ConstraintName,
                        PesoMeasurementRowEntityConfiguration.GlassCheckConstraintName,
                        StringComparison.Ordinal))
                {
                    failure = new ControloPersistenceException(
                        ControloPersistenceFailureReason.ResultNonPositive,
                        "A computed per-row result is not strictly positive; nothing was written.");
                    return true;
                }

                break;
        }

        return false;
    }

    private static async Task SafeRollbackAsync(
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Already completed: nothing to undo.
        }
        catch (Npgsql.PostgresException)
        {
            // The connection already reported the failed transaction state.
        }
    }

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Peso Project(PesoEntity entity, IReadOnlyList<PesoMeasurementRowEntity> rows) => new(
        DomainPesoId.From(entity.PesoId),
        entity.CmId,
        entity.ToolId,
        PesoStatusTokens.Parse(entity.Status) ?? PesoStatus.Pendente,
        entity.SubmittedAt,
        entity.SubmittedByUserId,
        entity.WaterTemperature,
        entity.VolumeMarisaBq,
        entity.VolumePuncaoPu,
        entity.GlassDensityGCm3,
        entity.PreviousProductionEndReference,
        entity.PreviousAverageWeightReference,
        entity.Version,
        entity.CreatedByUserId,
        entity.CreatedAt,
        entity.UpdatedAt,
        rows
            .Select(row => new PesoMeasurementRow(
                PesoMeasurementRowId.From(row.PesoMeasurementRowId),
                DomainPesoId.From(row.PesoId),
                row.RowPosition,
                row.WaterWeightG,
                row.CapacityCm3,
                row.GlassWeightG,
                row.CreatedAt))
            .OrderBy(row => row.RowPosition)
            .ToList());
}