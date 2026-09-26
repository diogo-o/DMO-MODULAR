using DMO.Application.ControloCreate;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;
using DMO.Domain.ControloComparacao;

namespace DMO.Application.ControloComparacao;

/// <summary>
/// The Peso Comparação service: the optional comparison child of the initial Peso — start a new
/// comparison event, select compared CMs, record NEW comparison measurements with the shared
/// Peso calculation path, decide each measured CM INDIVIDUALLY (final per event), confirm, and
/// read.
/// </summary>
/// <remarks>
/// <para>
/// Identity model: <c>peso_id</c> → the authoritative initial Peso control (untouched) and,
/// OPTIONALLY, <c>peso_id</c> → <c>comparacao_id</c> → one-or-more compared CM subjects keyed by
/// the natural <c>comparacao_id + cm_id</c>. A NEW comparison event always allocates a NEW
/// <c>comparacao_id</c>; the same CM of the same production is reused through the SAME
/// <c>cm_id</c> (the frozen <c>cm_contexts</c> identity — link to the JobOn context, canonical
/// <c>tool_id</c> and frozen tool snapshot); no CM identity is minted, copied or inferred from
/// reference/lot text and no Tool identity is duplicated.</para>
/// <para>
/// Calculation reuse: the comparison re-measurement computes every per-row result through the
/// SINGLE shared Peso row calculation path (<see cref="PesoRowCalculationRules"/>) — the same
/// authoritative formulas/rounding the initial Peso uses (no formula is duplicated anywhere).
/// The comparison conditions are the INITIAL Peso's frozen facts (its water temperature, its
/// drawing volumes and its FROZEN glass density): the re-measurement never invents conditions
/// and an unresolvable fact refuses with <c>calculation-configuration-missing</c>. Comparison
/// rows are persisted in the comparison tables only: they are NEW results that never contribute
/// to or modify the initial Peso average.</para>
/// <para>
/// Decision model: every measured compared CM requires its OWN explicit human decision
/// (<c>manter</c> / <c>colocar_de_parte</c>); decisions are individual per compared CM, never
/// global for the comparison; put-aside requires justification; maintaining requires no
/// fabricated reason; nothing is decided automatically and no Tool/warehouse/JobOn configuration
/// is ever changed automatically. The decision is FINAL inside one comparison event: a second
/// decision of the same compared CM is refused, so a prior decision is never overwritten at all
/// (silently or otherwise).</para>
/// <para>
/// Zero-effect guarantee: every Comparação write lives in the comparison tables only — the
/// initial Peso row (measurements, results, average, status, approval, PDF, history) is never
/// written by this service, and the initial Peso's general approve/reject lifecycle is untouched.</para>
/// </remarks>
public sealed class ControloComparacaoService : IControloComparacaoService
{
    private readonly IPesoRepository _pesos;
    private readonly IPesoContextRead _contextRead;
    private readonly IControloCalculationConfiguration _calculation;
    private readonly IComparacaoRepository _comparacoes;

    /// <summary>Creates the service over the shared Peso read, the cm-context traversal read, the
    /// water-density configuration and the comparison repository.</summary>
    public ControloComparacaoService(
        IPesoRepository pesos,
        IPesoContextRead contextRead,
        IControloCalculationConfiguration calculation,
        IComparacaoRepository comparacoes)
    {
        ArgumentNullException.ThrowIfNull(pesos);
        ArgumentNullException.ThrowIfNull(contextRead);
        ArgumentNullException.ThrowIfNull(calculation);
        ArgumentNullException.ThrowIfNull(comparacoes);
        _pesos = pesos;
        _contextRead = contextRead;
        _calculation = calculation;
        _comparacoes = comparacoes;
    }

    /// <inheritdoc />
    public async Task<ComparacaoResult> StartAsync(
        StartComparacaoCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloComparacaoValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new ComparacaoResult.ValidationFailed(errors);
        }

        var peso = await _pesos.GetByIdAsync(command.PesoId, cancellationToken);
        if (peso is null)
        {
            return new ComparacaoResult.PesoNotFound(command.PesoId);
        }

        var now = DateTimeOffset.UtcNow;
        var comparacao = new Comparacao(
            ComparacaoId.New(),
            PesoId.From(command.PesoId),
            command.CreatedByUserId,
            now,
            ConfirmedAt: null,
            ConfirmedByUserId: null,
            Version: 1,
            UpdatedAt: now,
            Subjects: []);

        try
        {
            var created = await _comparacoes.StartedAsync(comparacao, cancellationToken);

            return new ComparacaoResult.Started(created.ComparacaoId.Value, created.Version);
        }
        catch (ComparacaoPersistenceException exception)
        {
            return Map(exception);
        }
    }

    /// <inheritdoc />
    public async Task<ComparacaoResult> AddSubjectAsync(
        AddComparacaoCmSubjectCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloComparacaoValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new ComparacaoResult.ValidationFailed(errors);
        }

        var comparacao = await _comparacoes.GetByIdAsync(command.ComparacaoId, cancellationToken);
        if (comparacao is null)
        {
            return new ComparacaoResult.ComparacaoNotFound(command.ComparacaoId);
        }

        if (comparacao.ConfirmedAt is not null)
        {
            return Refuse(
                ComparacaoRefusalReason.AlreadyConfirmed,
                "This comparison event is already confirmed; no subject can be selected afterwards.");
        }

        // One subject per selected CM (the natural key comparison_id + cm_id).
        if (comparacao.Subjects.Any(subject => subject.CmId == command.CmId))
        {
            return new ComparacaoResult.ValidationFailed([ComparacaoValidationErrors.CmAlreadySelected]);
        }

        // The subject identity is verified against the REAL cm_contexts row (the same canonical
        // CM surface the initial Peso anchors to) — never inferred from reference/lot text.
        if (await _contextRead.GetCmContextAsync(command.CmId, cancellationToken) is null)
        {
            return new ComparacaoResult.ValidationFailed([ComparacaoValidationErrors.CmContextNotFound]);
        }

        var now = DateTimeOffset.UtcNow;
        var subject = new ComparacaoCmSubject(
            comparacao.ComparacaoId,
            command.CmId,
            command.CreatedByUserId,
            now,
            Decision: null,
            DecidedByUserId: null,
            DecidedAt: null,
            Reason: null,
            Version: 1,
            UpdatedAt: now,
            Rows: []);

        try
        {
            var updated = await _comparacoes.SubjectAddedAsync(
                subject,
                expectedComparacaoVersion: comparacao.Version,
                cancellationToken);

            return new ComparacaoResult.SubjectAdded(
                updated.ComparacaoId.Value,
                updated.Version,
                command.CmId);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(ComparacaoRefusalReason.StaleVersion, exception.Message);
        }
        catch (ComparacaoPersistenceException exception)
        {
            return Map(exception);
        }
    }

    /// <inheritdoc />
    public async Task<ComparacaoResult> RecordMeasurementsAsync(
        RecordComparacaoMeasurementsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloComparacaoValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new ComparacaoResult.ValidationFailed(errors);
        }

        var (comparacao, subject, refusal) = await LoadSubjectAsync(command.ComparacaoId, command.CmId, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        var stale = AssertCurrentSubjectVersion(subject!, command.ExpectedSubjectVersion);
        if (stale is not null)
        {
            return stale;
        }

        // The final decision binds the recorded rows: re-measuring after it is refused — nothing
        // is silently rewritten.
        if (subject!.Decision is not null)
        {
            return Refuse(
                ComparacaoRefusalReason.AlreadyDecided,
                "This compared CM already has its final individual decision; its committed " +
                "measurement rows are not replaced afterwards.");
        }

        // The comparison conditions are the INITIAL Peso's frozen facts — the authoritative
        // conditions of the re-measurement. A frozen fact that is missing (defensive only) fails
        // closed: no value is invented and nothing is written.
        var peso = await _pesos.GetByIdAsync(comparacao!.PesoId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Comparison '{comparacao.ComparacaoId}' anchors peso_id '{comparacao.PesoId}' which no " +
                "longer exists; the persisted anchor cannot be explained truthfully.");

        if (peso.GlassDensityGCm3 is not { } frozenDensity)
        {
            return Refuse(
                ComparacaoRefusalReason.CalculationConfigurationMissing,
                "The initial Peso carries no frozen glass density; the comparison measurement " +
                "cannot be derived. Nothing was written.");
        }

        if (!_calculation.TryGetWaterDensity(peso.WaterTemperature, out var waterDensity))
        {
            return Refuse(
                ComparacaoRefusalReason.CalculationConfigurationMissing,
                "The water density for the initial Peso's temperature is not resolvable; no value " +
                "is invented and nothing was written.");
        }

        var rows = PesoRowCalculationRules.ComputeRows(
            command.RowWaterWeightsG,
            waterDensity,
            frozenDensity,
            peso.VolumeMarisaBq,
            peso.VolumePuncaoPu,
            out var resultErrors);
        if (rows is null)
        {
            return new ComparacaoResult.ValidationFailed(resultErrors);
        }

        var now = DateTimeOffset.UtcNow;
        var recorded = subject with
        {
            Rows = ToRows(subject.ComparacaoId, subject.CmId, rows, now),
        };

        try
        {
            var updated = await _comparacoes.MeasurementsRecordedAsync(
                recorded,
                expectedSubjectVersion: subject.Version,
                cancellationToken);

            var saved = FindSubject(updated, subject.CmId)!;

            return new ComparacaoResult.MeasurementsRecorded(
                updated.ComparacaoId.Value,
                saved.CmId,
                saved.Version);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(ComparacaoRefusalReason.StaleVersion, exception.Message);
        }
        catch (ComparacaoPersistenceException exception)
        {
            return Map(exception);
        }
    }

    /// <inheritdoc />
    public async Task<ComparacaoResult> DecideAsync(
        DecideComparacaoCmCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloComparacaoValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new ComparacaoResult.ValidationFailed(errors);
        }

        var kind = ComparacaoCmDecisionKindTokens.Parse(command.Decision) ??
            throw new InvalidOperationException(
                "A validated decision command always carries a parseable decision token.");

        var (comparacao, subject, refusal) = await LoadSubjectAsync(command.ComparacaoId, command.CmId, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        var stale = AssertCurrentSubjectVersion(subject!, command.ExpectedSubjectVersion);
        if (stale is not null)
        {
            return stale;
        }

        // The decision is FINAL inside one comparison event: a second decision of the same
        // compared CM is refused — a prior decision is never overwritten (silently or otherwise).
        if (subject!.Decision is not null)
        {
            return Refuse(
                ComparacaoRefusalReason.AlreadyDecided,
                "This compared CM already has its final individual decision; a second decision is " +
                "refused inside the same comparison event.");
        }

        // The decision is individual per compared CM and can only decide a MEASURED subject: a
        // decision without NEW comparison measurements would decide nothing.
        if (!subject.HasMeasurements)
        {
            return Refuse(
                ComparacaoRefusalReason.SubjectNotMeasured,
                "This compared CM has no comparison measurements yet; record the re-measurement " +
                "before deciding it.");
        }

        var now = DateTimeOffset.UtcNow;
        var decided = subject with
        {
            Decision = kind,
            DecidedByUserId = command.DecidedByUserId,
            DecidedAt = now,
            Reason = kind == ComparacaoCmDecisionKind.ColocarDeParte ? command.Reason!.Trim() : TrimToNull(command.Reason),
        };

        try
        {
            var updated = await _comparacoes.DecidedAsync(
                decided,
                expectedSubjectVersion: subject.Version,
                cancellationToken);

            var saved = FindSubject(updated, subject.CmId)!;

            return new ComparacaoResult.Decided(
                updated.ComparacaoId.Value,
                saved.CmId,
                saved.Version,
                ComparacaoCmDecisionKindTokens.ToToken(kind),
                saved.DecidedAt ?? now);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(ComparacaoRefusalReason.StaleVersion, exception.Message);
        }
        catch (ComparacaoPersistenceException exception)
        {
            return Map(exception);
        }
    }

    /// <inheritdoc />
    public async Task<ComparacaoResult> ConfirmAsync(
        ConfirmComparacaoCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloComparacaoValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new ComparacaoResult.ValidationFailed(errors);
        }

        var comparacao = await _comparacoes.GetByIdAsync(command.ComparacaoId, cancellationToken);
        if (comparacao is null)
        {
            return new ComparacaoResult.ComparacaoNotFound(command.ComparacaoId);
        }

        if (comparacao.ConfirmedAt is not null)
        {
            return Refuse(
                ComparacaoRefusalReason.AlreadyConfirmed,
                "This comparison event is already confirmed; no second confirmation exists.");
        }

        if (comparacao.Subjects.Count == 0)
        {
            return Refuse(
                ComparacaoRefusalReason.NoCmSubjects,
                "This comparison event has no selected CM; nothing can be confirmed.");
        }

        // The functional-completeness rule: every MEASURED CM subject must carry its final
        // explicit individual decision before the comparison event can be treated as
        // complete/confirmed.
        var undecided = comparacao.Subjects
            .Where(subject => subject.HasMeasurements && !subject.IsDecided)
            .Select(subject => subject.CmId)
            .ToList();
        if (undecided.Count > 0)
        {
            return Refuse(
                ComparacaoRefusalReason.MeasurementsWithoutDecision,
                $"The comparison event cannot be confirmed while measured CM subjects lack their " +
                $"individual decisions: {string.Join(", ", undecided)}.");
        }

        var now = DateTimeOffset.UtcNow;
        var confirmed = comparacao with
        {
            ConfirmedAt = now,
            ConfirmedByUserId = command.ConfirmedByUserId,
        };

        try
        {
            var saved = await _comparacoes.ConfirmedAsync(
                confirmed,
                expectedComparacaoVersion: comparacao.Version,
                cancellationToken);

            return new ComparacaoResult.Confirmed(
                saved.ComparacaoId.Value,
                saved.Version,
                saved.ConfirmedAt ?? now);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(ComparacaoRefusalReason.StaleVersion, exception.Message);
        }
        catch (ComparacaoPersistenceException exception)
        {
            return Map(exception);
        }
    }

    /// <inheritdoc />
    public async Task<ComparacaoResult> GetAsync(
        Guid comparacaoId,
        CancellationToken cancellationToken)
    {
        var comparacao = await _comparacoes.GetByIdAsync(comparacaoId, cancellationToken);
        if (comparacao is null)
        {
            return new ComparacaoResult.ComparacaoNotFound(comparacaoId);
        }

        return new ComparacaoResult.Found(await BuildReadModelAsync(comparacao, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<ComparacaoResult> GetByPesoIdAsync(
        Guid pesoId,
        CancellationToken cancellationToken)
    {
        var comparacao = await _comparacoes.GetByPesoIdAsync(pesoId, cancellationToken);
        if (comparacao is null)
        {
            // The explicit absence: a Peso without a comparison is a fully valid normal Peso.
            return new ComparacaoResult.NoComparison(pesoId);
        }

        return new ComparacaoResult.Found(await BuildReadModelAsync(comparacao, cancellationToken));
    }

    // ---------------------------------------------------------------------------------------------
    // Shared loading and preconditions
    // ---------------------------------------------------------------------------------------------

    /// <summary>Loads the comparison event and its subject by the natural <c>comparacao_id + cm_id</c>
    /// identity and answers the shared preconditions (comparison exists, not confirmed).</summary>
    private async Task<(Comparacao? Comparacao, ComparacaoCmSubject? Subject, ComparacaoResult? Refusal)> LoadSubjectAsync(
        Guid comparacaoId,
        Guid cmId,
        CancellationToken cancellationToken)
    {
        var comparacao = await _comparacoes.GetByIdAsync(comparacaoId, cancellationToken);
        if (comparacao is null)
        {
            return (null, null, new ComparacaoResult.ComparacaoNotFound(comparacaoId));
        }

        if (comparacao.ConfirmedAt is not null)
        {
            return (comparacao, null, Refuse(
                ComparacaoRefusalReason.AlreadyConfirmed,
                "This comparison event is already confirmed; no subject mutation is accepted afterwards."));
        }

        var subject = FindSubject(comparacao, cmId);
        if (subject is null)
        {
            return (comparacao, null, new ComparacaoResult.CmSubjectNotFound(comparacaoId, cmId));
        }

        return (comparacao, subject, null);
    }

    private static ComparacaoCmSubject? FindSubject(Comparacao comparacao, Guid cmId)
    {
        foreach (var subject in comparacao.Subjects)
        {
            if (subject.CmId == cmId)
            {
                return subject;
            }
        }

        return null;
    }

    private ComparacaoResult? AssertCurrentSubjectVersion(ComparacaoCmSubject subject, int expectedVersion)
    {
        if (subject.Version != expectedVersion)
        {
            return Refuse(
                ComparacaoRefusalReason.StaleVersion,
                $"The compared CM '{subject.CmId}' changed after it was observed (expected version " +
                $"{expectedVersion}, current version {subject.Version}); nothing was written.");
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Read-model composition
    // ---------------------------------------------------------------------------------------------

    private async Task<ComparacaoReadModel> BuildReadModelAsync(
        Comparacao comparacao,
        CancellationToken cancellationToken)
    {
        var subjects = new List<ComparacaoCmSubjectReadModel>(comparacao.Subjects.Count);

        foreach (var subject in comparacao.Subjects.OrderBy(subject => subject.CreatedAt))
        {
            // The CM context projection resolves the canonical tool relationship through the
            // EXACT stored cm_id (the frozen production-context facts) — never from text and
            // never duplicated onto the subject.
            var context = await _contextRead.GetCmContextAsync(subject.CmId, cancellationToken);

            subjects.Add(new ComparacaoCmSubjectReadModel(
                subject.ComparacaoId.Value,
                subject.CmId,
                context,
                subject.Version,
                subject.CreatedByUserId,
                subject.CreatedAt,
                subject.Decision is { } decision
                    ? ComparacaoCmDecisionKindTokens.ToToken(decision)
                    : null,
                subject.DecidedByUserId,
                subject.DecidedAt,
                subject.Reason,
                subject.UpdatedAt,
                subject.Rows
                    .OrderBy(row => row.RowPosition)
                    .Select(row => new ComparacaoMeasurementRowReadModel(
                        row.RowPosition,
                        row.WaterWeightG,
                        row.CapacityCm3,
                        row.GlassWeightG))
                    .ToList()));
        }

        return new ComparacaoReadModel(
            comparacao.ComparacaoId.Value,
            comparacao.PesoId.Value,
            comparacao.Version,
            comparacao.CreatedByUserId,
            comparacao.CreatedAt,
            comparacao.ConfirmedAt,
            comparacao.ConfirmedByUserId,
            comparacao.UpdatedAt,
            subjects);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private static IReadOnlyList<ComparacaoMeasurementRow> ToRows(
        ComparacaoId comparacaoId,
        Guid cmId,
        IReadOnlyList<PesoRowCalculation> rows,
        DateTimeOffset createdAt) =>
        rows
            .Select(row => new ComparacaoMeasurementRow(
                comparacaoId,
                cmId,
                row.RowPosition,
                row.WaterWeightG,
                row.CapacityCm3,
                row.GlassWeightG,
                createdAt))
            .ToList();

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ComparacaoResult Refuse(ComparacaoRefusalReason reason, string message) =>
        new ComparacaoResult.Refused(reason, message);

    private static ComparacaoResult Map(ComparacaoPersistenceException exception) => exception.Reason switch
    {
        ComparacaoPersistenceFailureReason.ResultNonPositive => new ComparacaoResult.ValidationFailed(
            [ComparacaoValidationErrors.ResultNonPositive]),

        ComparacaoPersistenceFailureReason.ReasonRequired => new ComparacaoResult.ValidationFailed(
            [ComparacaoValidationErrors.PutAsideReasonRequired]),

        ComparacaoPersistenceFailureReason.DuplicateCmSelected => new ComparacaoResult.ValidationFailed(
            [ComparacaoValidationErrors.CmAlreadySelected]),

        // A referenced record or an impossible state observed at write time means the observed
        // aggregate is no longer current — the version guard is the primary protection and this
        // mapping surfaces the same stale-version posture (the accepted P2-T06 stance).
        ComparacaoPersistenceFailureReason.DependencyExists or ComparacaoPersistenceFailureReason.InvalidDecisionState => Refuse(
            ComparacaoRefusalReason.StaleVersion,
            exception.Message),

        _ => throw exception,
    };
}