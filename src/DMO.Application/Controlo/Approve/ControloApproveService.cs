using DMO.Application.Controlo.Pesos;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Application.Session;
using DMO.Domain.Controlo;

namespace DMO.Application.Controlo.Approve;

/// <summary>
/// The P2-T06 review/decision service: approve/reject/reopen on the SAME <c>peso_id</c> with
/// backend attribution, the pending/history list queries and the exact-record review composed
/// from the shared P2-T05 read model.
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract §9/§10/§11/§15/§19. The service composes the SHARED application
/// read (<c>IControloCreateService.GetAsync</c> — the only Peso facts source, RD1), the Peso
/// read (<c>IPesoRepository</c>, closed input consumed read-only), the review repository (list
/// queries + the decision write) and <c>ICurrentAccountContext</c> (the backend actor — never a
/// command field, AC-D4). State preconditions are refused here from the persisted Peso read; the
/// repository's version guard is the racing backstop (every decision transition bumps the
/// version). P2-T06 is never an editor of measurement facts: write paths touch only
/// <c>pesos.status</c>/<c>version</c>/<c>updated_at</c> (+ <c>submitted_at</c>/
/// <c>submitted_by_user_id</c> cleared on reopen) and the decision trail (§19.5, AC-R1/R2/O5).
/// </remarks>
public sealed class ControloApproveService : IControloApproveService
{
    private readonly IControloCreateService _create;
    private readonly IPesoRepository _pesos;
    private readonly IPesoReviewRepository _review;
    private readonly ICurrentAccountContext _currentAccount;

    /// <summary>Creates the service over the shared read, the Peso read and the review repository.</summary>
    public ControloApproveService(
        IControloCreateService create,
        IPesoRepository pesos,
        IPesoReviewRepository review,
        ICurrentAccountContext currentAccount)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(pesos);
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(currentAccount);
        _create = create;
        _pesos = pesos;
        _review = review;
        _currentAccount = currentAccount;
    }

    /// <inheritdoc />
    public async Task<ReviewResult> GetPendingAsync(
        PendingListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = ControloApproveValidator.Validate(query);
        if (errors.Count > 0)
        {
            return new ReviewResult.ValidationFailed(errors);
        }

        var rows = await _review.GetPendingAsync(query, cancellationToken);

        // The Total metadata is the backend-counted row count of the returned page (the exact
        // repository carrier of §8.2 carries no cross-page total; the DenseDataTable paging
        // presentation is consumer-owned and renders page labels only — never a fabricated
        // grand total).
        return new ReviewResult.PendingFound(
            rows.Select(ToPendingItem).ToList(),
            rows.Count);
    }

    /// <inheritdoc />
    public async Task<ReviewResult> GetHistoryAsync(
        HistoryListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = ControloApproveValidator.Validate(query);
        if (errors.Count > 0)
        {
            return new ReviewResult.ValidationFailed(errors);
        }

        var rows = await _review.GetHistoryAsync(query, cancellationToken);

        return new ReviewResult.HistoryFound(
            rows.Select(ToHistoryItem).ToList(),
            rows.Count);
    }

    /// <inheritdoc />
    public async Task<ReviewResult> GetReviewSheetAsync(
        Guid pesoId,
        CancellationToken cancellationToken)
    {
        var read = await _create.GetAsync(pesoId, cancellationToken);
        if (read is not PesoResult.Found(var sheet))
        {
            return new ReviewResult.NotFound(pesoId);
        }

        var decisions = await _review.GetDecisionsAsync(pesoId, cancellationToken);
        var available = BuildAvailability(sheet);

        return new ReviewResult.ReviewSheet(new ReviewSheetReadModel(
            sheet,
            decisions.Select(ToDecisionItem).ToList(),
            available));
    }

    /// <inheritdoc />
    public async Task<ReviewResult> GetDecisionsAsync(
        Guid pesoId,
        CancellationToken cancellationToken)
    {
        var peso = await _pesos.GetByIdAsync(pesoId, cancellationToken);
        if (peso is null)
        {
            return new ReviewResult.NotFound(pesoId);
        }

        var decisions = await _review.GetDecisionsAsync(pesoId, cancellationToken);

        return new ReviewResult.DecisionsFound(decisions.Select(ToDecisionItem).ToList());
    }

    /// <inheritdoc />
    public async Task<ReviewResult> ApproveAsync(
        ApprovePesoCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloApproveValidator.ValidateApprove(command);
        if (errors.Count > 0)
        {
            return new ReviewResult.ValidationFailed(errors);
        }

        var (peso, precondition) = await LoadForDecisionAsync(command.PesoId, command.ExpectedVersion, cancellationToken);
        if (precondition is not null)
        {
            return precondition;
        }

        if (peso!.Status != PesoStatus.Pendente)
        {
            return Refuse(
                ReviewRefusalReason.AlreadyDecided,
                "Este Peso já foi decidido; não é criado um segundo evento e o estado não volta a mudar.");
        }

        try
        {
            var decision = await DecideAsync(
                command.PesoId,
                PesoReviewDecisionKind.Aprovado,
                reason: null,
                peso,
                cancellationToken);

            return new ReviewResult.Approved(
                decision.PesoId.Value,
                decision.PesoVersionAtDecision + 1,
                decision.DecidedAt);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(ReviewRefusalReason.StaleVersion, exception.Message);
        }
        catch (PesoReviewPersistenceException exception) when (exception.Reason == PesoReviewPersistenceFailureReason.DependencyExists)
        {
            return Refuse(ReviewRefusalReason.StaleVersion, exception.Message);
        }
    }

    /// <inheritdoc />
    public async Task<ReviewResult> RejectAsync(
        RejectPesoCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloApproveValidator.ValidateReject(command);
        if (errors.Count > 0)
        {
            return new ReviewResult.ValidationFailed(errors);
        }

        var (peso, precondition) = await LoadForDecisionAsync(command.PesoId, command.ExpectedVersion, cancellationToken);
        if (precondition is not null)
        {
            return precondition;
        }

        if (peso!.Status != PesoStatus.Pendente)
        {
            return Refuse(
                ReviewRefusalReason.AlreadyDecided,
                "Este Peso já foi decidido; não é criado um segundo evento e o estado não volta a mudar.");
        }

        try
        {
            var decision = await DecideAsync(
                command.PesoId,
                PesoReviewDecisionKind.NaoAprovado,
                command.Reason.Trim(),
                peso,
                cancellationToken);

            return new ReviewResult.Rejected(
                decision.PesoId.Value,
                decision.PesoVersionAtDecision + 1,
                decision.DecidedAt);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(ReviewRefusalReason.StaleVersion, exception.Message);
        }
        catch (PesoReviewPersistenceException)
        {
            // The reason CHECK backstop maps onto the SAME validator token (never a 500).
            return new ReviewResult.ValidationFailed(
                [ControloApproveValidationErrors.RejectReasonRequired]);
        }
    }

    /// <inheritdoc />
    public async Task<ReviewResult> ReopenAsync(
        ReopenPesoCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloApproveValidator.ValidateReopen(command);
        if (errors.Count > 0)
        {
            return new ReviewResult.ValidationFailed(errors);
        }

        var (peso, precondition) = await LoadForDecisionAsync(command.PesoId, command.ExpectedVersion, cancellationToken);
        if (precondition is not null)
        {
            return precondition;
        }

        // Reopen is allowed for a record IN the review circuit: a submitted-undecided record or a
        // decided record. A draft (never submitted) is Create's object — nothing to reopen.
        if (peso!.SubmittedAt is null)
        {
            return Refuse(
                ReviewRefusalReason.NotReviewable,
                "Este Peso nunca foi submetido para aprovação; não há nada a reabrir (o rascunho é editado em Controlo_Create).");
        }

        try
        {
            var decision = await DecideAsync(
                command.PesoId,
                PesoReviewDecisionKind.Reaberto,
                command.Reason.Trim(),
                peso,
                cancellationToken);

            return new ReviewResult.Reopened(
                decision.PesoId.Value,
                decision.PesoVersionAtDecision + 1,
                decision.DecidedAt);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(ReviewRefusalReason.StaleVersion, exception.Message);
        }
        catch (PesoReviewPersistenceException)
        {
            // The reason CHECK backstop maps onto the SAME validator token (never a 500).
            return new ReviewResult.ValidationFailed(
                [ControloApproveValidationErrors.ReopenReasonRequired]);
        }
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Loads the persisted Peso and answers the shared preconditions: exists, version current,
    /// and entered the review circuit (submitted). Decided-state checks belong to each decision.
    /// </summary>
    private async Task<(Peso? Peso, ReviewResult? Refusal)> LoadForDecisionAsync(
        Guid pesoId,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var peso = await _pesos.GetByIdAsync(pesoId, cancellationToken);
        if (peso is null)
        {
            return (null, new ReviewResult.NotFound(pesoId));
        }

        if (peso.Version != expectedVersion)
        {
            return (peso, Refuse(
                ReviewRefusalReason.StaleVersion,
                $"O Peso foi alterado depois de observado (versão esperada {expectedVersion}, versão atual {peso.Version}); nada foi guardado."));
        }

        if (peso!.SubmittedAt is null)
        {
            return (peso, Refuse(
                ReviewRefusalReason.NotReviewable,
                "Este Peso nunca foi submetido para aprovação (não entrou no circuito de revisão); pertence a Controlo_Create."));
        }

        return (peso, null);
    }

    /// <summary>
    /// Applies the decision atomically (transition + append-only event, ONE transaction, §10):
    /// actor/time are backend facts (current USER + backend clock, AC-D4).
    /// </summary>
    private async Task<PesoReviewDecision> DecideAsync(
        Guid pesoId,
        PesoReviewDecisionKind kind,
        string? reason,
        Peso peso,
        CancellationToken cancellationToken)
    {
        var actor = await ResolveCurrentUserIdAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The decision route requires an authenticated USER account; the actor is a backend " +
                "fact and is never client-supplied.");

        var now = DateTimeOffset.UtcNow;

        var decision = new PesoReviewDecision(
            PesoReviewDecisionId.New(),
            PesoId.From(pesoId),
            kind,
            actor,
            now,
            reason,
            peso.Status,
            peso.Version,
            now);

        return await _review.DecisionAsync(decision, expectedPesoVersion: peso.Version, cancellationToken);
    }

    private static ReviewAvailability BuildAvailability(PesoSheetReadModel sheet)
    {
        var pendenteToken = PesoStatusTokens.ToToken(PesoStatus.Pendente);
        var decided = !string.Equals(sheet.Status, pendenteToken, StringComparison.Ordinal)
            && (string.Equals(sheet.Status, PesoStatusTokens.ToToken(PesoStatus.Aprovado), StringComparison.Ordinal)
                || string.Equals(sheet.Status, PesoStatusTokens.ToToken(PesoStatus.NaoAprovado), StringComparison.Ordinal));

        if (sheet.SubmittedAt is not null && string.Equals(sheet.Status, pendenteToken, StringComparison.Ordinal))
        {
            return ReviewAvailability.Reviewable();
        }

        return decided ? ReviewAvailability.Decided() : ReviewAvailability.Draft();
    }

    private static PendingItemReadModel ToPendingItem(PesoReviewRow row) => new(
        row.PesoId,
        row.Version,
        row.Status,
        row.CmId,
        row.ToolId,
        row.Reference,
        row.ProductionNumber,
        row.Machine,
        row.ToolReference,
        row.Processo,
        row.SubmittedAt!.Value,
        row.SubmittedByUserId!.Value,
        row.RowCount,
        row.HasDecisionTrail);

    private static HistoryItemReadModel ToHistoryItem(PesoReviewRow row) => new(
        row.PesoId,
        row.Version,
        row.Status,
        row.CmId,
        row.ToolId,
        row.Reference,
        row.ProductionNumber,
        row.Machine,
        row.ToolReference,
        row.Processo,
        row.SubmittedAt,
        row.SubmittedByUserId,
        row.LastDecision,
        row.DecisionCount);

    private static DecisionItemReadModel ToDecisionItem(PesoReviewDecision decision) => new(
        decision.PesoReviewDecisionId.Value,
        PesoReviewDecisionKindTokens.ToToken(decision.Decision),
        decision.DecidedByUserId,
        decision.DecidedAt,
        decision.Reason,
        PesoStatusTokens.ToToken(decision.PriorStatus),
        decision.PesoVersionAtDecision);

    private async Task<Guid?> ResolveCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        var current = await _currentAccount.GetCurrentAsync(cancellationToken);

        return current is CurrentAccount.User(var account)
            ? account.AccountId
            : null;
    }

    private static ReviewResult Refuse(ReviewRefusalReason reason, string message) =>
        new ReviewResult.Refused(reason, message);
}