using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.ControloComparacao;
using DMO.Infrastructure.Persistence.Entities;
using DMO.Infrastructure.Persistence.EntityConfigurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using DomainComparacaoId = DMO.Domain.ControloComparacao.ComparacaoId;
using DomainPesoId = DMO.Domain.Controlo.PesoId;

namespace DMO.Infrastructure.Persistence;

/// <summary>
/// <see cref="IComparacaoRepository"/> implementation over the single application persistence
/// context: the Peso Comparação aggregate over its OWN three tables.
/// </summary>
/// <remarks>
/// <para>
/// Every write opens its own transaction. The subject identity is the natural
/// <c>(comparacao_id, cm_id)</c> composite key and rows use
/// <c>(comparacao_id, cm_id, row_position)</c> — no surrogate subject identity and no reading
/// UUID exist. The initial Peso table is NEVER written here: the comparison aggregate cannot
/// mutate the Peso's measurements, results, status, approval or version (zero-effect rule).</para>
/// <para>
/// The typed failure mapping follows the accepted Peso convention: RESTRICT FK violations
/// (vanished Peso/CM context/actor) → <c>DependencyExists</c>; the reason and decision-state
/// CHECK backstops → <c>ReasonRequired</c>/<c>InvalidDecisionState</c>; the composite-subject
/// primary-key duplication → <c>DuplicateCmSelected</c>; the row-result CHECKs →
/// <c>ResultNonPositive</c> (C2 — never a 500). The <c>version</c> tokens (event + subject)
/// stay active through the accepted <see cref="SaveAsync"/> pattern (compare + EF-token race →
/// <c>ConcurrencyConflictException</c> → 409 <c>stale-version</c>).</para>
/// </remarks>
public sealed class ComparacaoRepository : IComparacaoRepository
{
    private readonly DmoDbContext _context;

    /// <summary>Creates the repository over the application persistence context.</summary>
    public ComparacaoRepository(DmoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<Comparacao?> GetByIdAsync(Guid comparacaoId, CancellationToken cancellationToken)
    {
        var entity = await Comparacoes
            .AsNoTracking()
            .FirstOrDefaultAsync(comparacao => comparacao.ComparacaoId == comparacaoId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var subjects = await Subjects
            .AsNoTracking()
            .Where(subject => subject.ComparacaoId == comparacaoId)
            .OrderBy(subject => subject.CreatedAt)
            .ToListAsync(cancellationToken);

        var rows = await Rows
            .AsNoTracking()
            .Where(row => row.ComparacaoId == comparacaoId)
            .OrderBy(row => row.RowPosition)
            .ToListAsync(cancellationToken);

        return Project(entity, subjects, rows);
    }

    /// <inheritdoc />
    public async Task<Comparacao?> GetByPesoIdAsync(Guid pesoId, CancellationToken cancellationToken)
    {
        // A Peso may have several comparison events; the newest (by creation, then id) is the
        // current one — the deliberate single-carrier read of this contract.
        var entity = await Comparacoes
            .AsNoTracking()
            .Where(comparacao => comparacao.PesoId == pesoId)
            .OrderByDescending(comparacao => comparacao.CreatedAt)
            .ThenByDescending(comparacao => comparacao.ComparacaoId)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return null;
        }

        return await GetByIdAsync(entity.ComparacaoId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Comparacao> StartedAsync(
        Comparacao comparacao,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comparacao);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            Comparacoes.Add(new ComparacaoEntity
            {
                ComparacaoId = comparacao.ComparacaoId.Value,
                PesoId = comparacao.PesoId.Value,
                CreatedByUserId = comparacao.CreatedByUserId,
                CreatedAt = comparacao.CreatedAt,
                ConfirmedAt = null,
                ConfirmedByUserId = null,
                Version = 1,
                UpdatedAt = now,
            });

            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryMapWriteFailure(exception, out var failure))
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            _context.ChangeTracker.Clear();
            throw failure;
        }

        return await GetByIdAsync(comparacao.ComparacaoId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The started comparison '{comparacao.ComparacaoId}' could not be read back after commit.");
    }

    /// <inheritdoc />
    public async Task<Comparacao> SubjectAddedAsync(
        ComparacaoCmSubject subject,
        int expectedComparacaoVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await LoadTrackedComparacaoAsync(subject.ComparacaoId.Value, cancellationToken)
                ?? throw new ConcurrencyConflictException(
                    $"Comparison '{subject.ComparacaoId}' no longer exists (deleted concurrently); nothing was written.");

            AssertComparacaoVersion(entity, subject.ComparacaoId.Value, expectedComparacaoVersion);

            Subjects.Add(new ComparacaoCmSubjectEntity
            {
                ComparacaoId = subject.ComparacaoId.Value,
                CmId = subject.CmId,
                CreatedByUserId = subject.CreatedByUserId,
                CreatedAt = subject.CreatedAt,
                Decision = null,
                Reason = null,
                DecidedByUserId = null,
                DecidedAt = null,
                Version = 1,
                UpdatedAt = now,
            });

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

        return await GetByIdAsync(subject.ComparacaoId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The comparison '{subject.ComparacaoId}' could not be read back after the subject add.");
    }

    /// <inheritdoc />
    public async Task<Comparacao> MeasurementsRecordedAsync(
        ComparacaoCmSubject subject,
        int expectedSubjectVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await LoadTrackedSubjectAsync(subject.ComparacaoId.Value, subject.CmId, cancellationToken)
                ?? throw new ConcurrencyConflictException(
                    $"Compared CM '{subject.CmId}' of comparison '{subject.ComparacaoId}' no longer exists " +
                    "(removed concurrently); nothing was written.");

            AssertSubjectVersion(entity, subject.ComparacaoId.Value, subject.CmId, expectedSubjectVersion);

            // Whole-set row replacement: the measurement-row set is an aggregate part of the subject.
            var existing = await Rows
                .Where(row => row.ComparacaoId == subject.ComparacaoId.Value && row.CmId == subject.CmId)
                .ToListAsync(cancellationToken);
            if (existing.Count > 0)
            {
                Rows.RemoveRange(existing);
            }

            foreach (var row in subject.Rows)
            {
                InsertRow(subject.ComparacaoId.Value, subject.CmId, row, now);
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

        return await GetByIdAsync(subject.ComparacaoId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The comparison '{subject.ComparacaoId}' could not be read back after the measurement recording.");
    }

    /// <inheritdoc />
    public async Task<Comparacao> DecidedAsync(
        ComparacaoCmSubject subject,
        int expectedSubjectVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await LoadTrackedSubjectAsync(subject.ComparacaoId.Value, subject.CmId, cancellationToken)
                ?? throw new ConcurrencyConflictException(
                    $"Compared CM '{subject.CmId}' of comparison '{subject.ComparacaoId}' no longer exists " +
                    "(removed concurrently); nothing was written.");

            AssertSubjectVersion(entity, subject.ComparacaoId.Value, subject.CmId, expectedSubjectVersion);

            // The FINAL individual decision state — applied once (a second decision is refused by
            // the service; the state CHECKs back the invariants below).
            entity.Decision = subject.Decision is { } kind
                ? ComparacaoCmDecisionKindTokens.ToToken(kind)
                : null;
            entity.Reason = subject.Reason;
            entity.DecidedByUserId = subject.DecidedByUserId;
            entity.DecidedAt = subject.DecidedAt;
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

        return await GetByIdAsync(subject.ComparacaoId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The comparison '{subject.ComparacaoId}' could not be read back after the decision.");
    }

    /// <inheritdoc />
    public async Task<Comparacao> ConfirmedAsync(
        Comparacao comparacao,
        int expectedComparacaoVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comparacao);

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await LoadTrackedComparacaoAsync(comparacao.ComparacaoId.Value, cancellationToken)
                ?? throw new ConcurrencyConflictException(
                    $"Comparison '{comparacao.ComparacaoId}' no longer exists (deleted concurrently); nothing was written.");

            AssertComparacaoVersion(entity, comparacao.ComparacaoId.Value, expectedComparacaoVersion);

            entity.ConfirmedAt = comparacao.ConfirmedAt;
            entity.ConfirmedByUserId = comparacao.ConfirmedByUserId;
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

        return await GetByIdAsync(comparacao.ComparacaoId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The confirmed comparison '{comparacao.ComparacaoId}' could not be read back after commit.");
    }

    // ---------------------------------------------------------------------------------------------
    // Query surface
    // ---------------------------------------------------------------------------------------------

    private DbSet<ComparacaoEntity> Comparacoes => _context.Set<ComparacaoEntity>();

    private DbSet<ComparacaoCmSubjectEntity> Subjects => _context.Set<ComparacaoCmSubjectEntity>();

    private DbSet<ComparacaoMeasurementRowEntity> Rows => _context.Set<ComparacaoMeasurementRowEntity>();

    private async Task<ComparacaoEntity?> LoadTrackedComparacaoAsync(Guid comparacaoId, CancellationToken cancellationToken) =>
        await Comparacoes.FirstOrDefaultAsync(comparacao => comparacao.ComparacaoId == comparacaoId, cancellationToken);

    private async Task<ComparacaoCmSubjectEntity?> LoadTrackedSubjectAsync(
        Guid comparacaoId,
        Guid cmId,
        CancellationToken cancellationToken) =>
        await Subjects.FirstOrDefaultAsync(
            subject => subject.ComparacaoId == comparacaoId && subject.CmId == cmId,
            cancellationToken);

    private static void AssertComparacaoVersion(ComparacaoEntity entity, Guid comparacaoId, int expectedVersion)
    {
        if (entity.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                $"Comparison '{comparacaoId}' was modified concurrently (expected version {expectedVersion}, " +
                $"current version {entity.Version}); reload and retry.");
        }
    }

    private static void AssertSubjectVersion(ComparacaoCmSubjectEntity entity, Guid comparacaoId, Guid cmId, int expectedVersion)
    {
        if (entity.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                $"Compared CM '{cmId}' of comparison '{comparacaoId}' was modified concurrently (expected " +
                $"version {expectedVersion}, current version {entity.Version}); reload and retry.");
        }
    }

    private void InsertRow(Guid comparacaoId, Guid cmId, ComparacaoMeasurementRow row, DateTimeOffset now)
    {
        Rows.Add(new ComparacaoMeasurementRowEntity
        {
            ComparacaoId = comparacaoId,
            CmId = cmId,
            RowPosition = row.RowPosition,
            WaterWeightG = row.WaterWeightG,
            CapacityCm3 = row.CapacityCm3,
            GlassWeightG = row.GlassWeightG,
            CreatedAt = now,
        });
    }

    /// <summary>
    /// Saves the tracked changes, mapping a save-time optimistic-concurrency conflict onto the
    /// domain typed conflict (the accepted <c>SaveAsync</c> pattern): the explicit in-transaction
    /// version compare closes already-stale requests, while the active <c>IsConcurrencyToken</c>
    /// guard closes the remaining race between that compare and this save.
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
    /// propagates unchanged (the accepted Peso binding rules).
    /// </summary>
    private static bool TryMapWriteFailure(DbUpdateException exception, out ComparacaoPersistenceException failure)
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
            case "23503": // foreign_key_violation — every Comparação FK is RESTRICT.
                failure = new ComparacaoPersistenceException(
                    ComparacaoPersistenceFailureReason.DependencyExists,
                    "A referenced record (the Peso, a CM context or an actor) still depends on this " +
                    "write target or vanished concurrently; nothing was written.");
                return true;

            case "23514": // check_violation — the validator backstops.
                if (string.Equals(
                        postgresException.ConstraintName,
                        ComparacaoCmSubjectEntityConfiguration.ReasonCheckConstraintName,
                        StringComparison.Ordinal))
                {
                    failure = new ComparacaoPersistenceException(
                        ComparacaoPersistenceFailureReason.ReasonRequired,
                        "The put-aside decision requires its justification; nothing was written.");
                    return true;
                }

                if (string.Equals(
                        postgresException.ConstraintName,
                        ComparacaoCmSubjectEntityConfiguration.DecisionStateCheckConstraintName,
                        StringComparison.Ordinal))
                {
                    failure = new ComparacaoPersistenceException(
                        ComparacaoPersistenceFailureReason.InvalidDecisionState,
                        "The per-CM decision state is inconsistent; nothing was written.");
                    return true;
                }

                if (string.Equals(
                        postgresException.ConstraintName,
                        ComparacaoMeasurementRowEntityConfiguration.CapacityCheckConstraintName,
                        StringComparison.Ordinal)
                    || string.Equals(
                        postgresException.ConstraintName,
                        ComparacaoMeasurementRowEntityConfiguration.GlassCheckConstraintName,
                        StringComparison.Ordinal))
                {
                    failure = new ComparacaoPersistenceException(
                        ComparacaoPersistenceFailureReason.ResultNonPositive,
                        "A computed comparison result is not strictly positive; nothing was written.");
                    return true;
                }

                break;

            case "23505": // unique_violation — the natural-key invariants (defensive backstops).
                if (string.Equals(
                        postgresException.ConstraintName,
                        "comparacao_cm_subjects_pkey",
                        StringComparison.Ordinal))
                {
                    failure = new ComparacaoPersistenceException(
                        ComparacaoPersistenceFailureReason.DuplicateCmSelected,
                        "The CM is already selected in this comparison event; nothing was written.");
                    return true;
                }

                // Any other duplicate (row-position key, header id) is an application bug: fail
                // closed like the accepted review convention.
                failure = new ComparacaoPersistenceException(
                    ComparacaoPersistenceFailureReason.DependencyExists,
                    "A duplicate comparison identity was attempted; nothing was written.");
                return true;
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

    private static Comparacao Project(
        ComparacaoEntity entity,
        IReadOnlyList<ComparacaoCmSubjectEntity> subjects,
        IReadOnlyList<ComparacaoMeasurementRowEntity> rows)
    {
        var rowsBySubject = rows
            .GroupBy(row => (row.ComparacaoId, row.CmId))
            .ToDictionary(group => group.Key, group => group.OrderBy(row => row.RowPosition).ToList());

        return new Comparacao(
            DomainComparacaoId.From(entity.ComparacaoId),
            DomainPesoId.From(entity.PesoId),
            entity.CreatedByUserId,
            entity.CreatedAt,
            entity.ConfirmedAt,
            entity.ConfirmedByUserId,
            entity.Version,
            entity.UpdatedAt,
            subjects
                .OrderBy(subject => subject.CreatedAt)
                .Select(subject =>
                {
                    var subjectRows = rowsBySubject.GetValueOrDefault((subject.ComparacaoId, subject.CmId)) ?? [];

                    return new ComparacaoCmSubject(
                        DomainComparacaoId.From(subject.ComparacaoId),
                        subject.CmId,
                        subject.CreatedByUserId,
                        subject.CreatedAt,
                        ComparacaoCmDecisionKindTokens.Parse(subject.Decision),
                        subject.DecidedByUserId,
                        subject.DecidedAt,
                        subject.Reason,
                        subject.Version,
                        subject.UpdatedAt,
                        subjectRows
                            .Select(row => new ComparacaoMeasurementRow(
                                DomainComparacaoId.From(row.ComparacaoId),
                                row.CmId,
                                row.RowPosition,
                                row.WaterWeightG,
                                row.CapacityCm3,
                                row.GlassWeightG,
                                row.CreatedAt))
                            .ToList());
                })
                .ToList());
    }
}