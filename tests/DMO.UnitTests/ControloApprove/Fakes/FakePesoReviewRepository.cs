using DMO.Application.Controlo.Approve;
using DMO.Application.Controlo.Pesos;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;
using PesoId = DMO.Domain.Controlo.PesoId;

namespace DMO.UnitTests.ControloApprove.Fakes;

/// <summary>
/// In-memory P2-T06 review repository fake for the unit/service tests: enforces the version guard
/// and applies the contracted transitions (approve/reject/reopen) exactly like
/// <c>PesoReviewRepository.DecisionAsync</c>, so the service-level proofs exercise real semantics
/// without a database.
/// </summary>
internal sealed class FakePesoReviewRepository : IPesoReviewRepository
{
    private readonly List<PesoReviewDecision> _decisions = [];
    private readonly Dictionary<Guid, PesoStatus> _statuses = [];

    /// <summary>The decisions recorded in insertion order (append-only).</summary>
    public IReadOnlyList<PesoReviewDecision> Decisions => _decisions;

    /// <summary>Whether a decision write was ever attempted.</summary>
    public int WriteAttempts { get; private set; }

    /// <summary>Registers the initial Peso state the fake guards (version starts at 1).</summary>
    public void Seed(Guid pesoId, PesoStatus status)
    {
        _statuses[pesoId] = status;
    }

    public Task<IReadOnlyList<PesoReviewRow>> GetPendingAsync(
        PendingListQuery query,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PesoReviewRow>>([]);

    public Task<IReadOnlyList<PesoReviewRow>> GetHistoryAsync(
        HistoryListQuery query,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PesoReviewRow>>([]);

    public Task<IReadOnlyList<PesoReviewDecision>> GetDecisionsAsync(
        Guid pesoId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PesoReviewDecision>>(
            _decisions
                .Where(decision => decision.PesoId.Value == pesoId)
                .OrderBy(decision => decision.DecidedAt)
                .ToList());

    public Task<PesoReviewDecision> DecisionAsync(
        PesoReviewDecision decision,
        int expectedPesoVersion,
        CancellationToken cancellationToken)
    {
        WriteAttempts += 1;

        if (!_statuses.TryGetValue(decision.PesoId.Value, out _))
        {
            throw new ConcurrencyConflictException("The Peso no longer exists.");
        }

        if (decision.PesoVersionAtDecision != expectedPesoVersion)
        {
            throw new ConcurrencyConflictException(
                $"The Peso was modified concurrently (expected version {expectedPesoVersion}, " +
                $"current version {decision.PesoVersionAtDecision}); nothing was written.");
        }

        _decisions.Add(decision);

        switch (decision.Decision)
        {
            case PesoReviewDecisionKind.Aprovado:
                _statuses[decision.PesoId.Value] = PesoStatus.Aprovado;
                break;

            case PesoReviewDecisionKind.NaoAprovado:
                _statuses[decision.PesoId.Value] = PesoStatus.NaoAprovado;
                break;

            case PesoReviewDecisionKind.Reaberto:
                _statuses[decision.PesoId.Value] = PesoStatus.Pendente;
                break;
        }

        return Task.FromResult(decision);
    }
}

/// <summary>
/// In-memory Peso repository fake: serves the persisted Peso (full row set) the decision service
/// reads for its state preconditions.
/// </summary>
internal sealed class FakePesoRepository : IPesoRepository
{
    private readonly Dictionary<Guid, Peso> _pesos = [];

    public void Seed(Peso peso) => _pesos[peso.PesoId.Value] = peso;

    public Task<Peso?> GetByIdAsync(Guid pesoId, CancellationToken cancellationToken) =>
        Task.FromResult(_pesos.TryGetValue(pesoId, out var peso) ? peso : null);

    public Task<Peso> CreatedAsync(
        Peso peso,
        IReadOnlyList<PesoMeasurementRow> rows,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The review unit tests never create Pesos.");

    public Task<Peso> UpdatedAsync(
        Peso peso,
        IReadOnlyList<PesoMeasurementRow> rows,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The review unit tests never edit Pesos.");

    public Task<Peso> SubmittedAsync(Peso peso, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The review unit tests never submit Pesos.");

    public Task<Peso> AssociatedAsync(Peso peso, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The review unit tests never associate Pesos.");
}

/// <summary>
/// In-memory shared read fake: answers the review-sheet read with a supplied
/// <c>PesoSheetReadModel</c> (or <c>NotFound</c>) — the ONLY Peso facts source of the review
/// (RD1: the service composes the shared application read, never a P2-T06 Peso read).
/// </summary>
internal sealed class FakeControloCreateService : IControloCreateService
{
    private readonly PesoSheetReadModel? _sheet;

    public FakeControloCreateService(PesoSheetReadModel? sheet = null)
    {
        _sheet = sheet;
    }

    public Task<PesoResult> CalculateAsync(
        CalculatePesoCommand command,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The review unit tests never calculate.");

    public Task<PesoResult> CreateAsync(
        CreatePesoCommand command,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The review unit tests never create.");

    public Task<PesoResult> GetAsync(Guid pesoId, CancellationToken cancellationToken) =>
        Task.FromResult<PesoResult>(
            _sheet is null || _sheet.PesoId != pesoId
                ? new PesoResult.NotFound(pesoId)
                : new PesoResult.Found(_sheet));

    public Task<PesoResult> UpdateAsync(
        UpdatePesoCommand command,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The review unit tests never edit.");

    public Task<PesoResult> SubmitAsync(
        SubmitPesoCommand command,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The review unit tests never submit.");

    public Task<PesoResult> AssociateAsync(
        AssociatePesoCommand command,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The review unit tests never associate.");

    public Task<PesoResult> ListAssociationCandidatesAsync(
        Guid toolId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The review unit tests never list candidates.");
}