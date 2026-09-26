using DMO.Application.Controlo.Approve;
using DMO.Application.JobOn;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Controlo;
using DMO.Domain.JobOn;
using DMO.Domain.Tools;
using DMO.IntegrationTests.Controlo.Pesos;
using PesoId = DMO.Domain.Controlo.PesoId;

namespace DMO.IntegrationTests.Controlo.Approve;

/// <summary>
/// In-memory P2-T06 review repository for the HTTP-class tests: mirrors the contracted semantics
/// of <c>PesoReviewRepository</c> over the <see cref="P2T05TestComposition"/> Peso store (the
/// reviewable predicate, the local-Histórico visibility predicate, the traversal filters, the
/// version-guarded decision write with the exact transitions and the append-only trail).
/// </summary>
/// <remarks>
/// The schema itself is proven separately by the env-gated DB-class tests
/// (<c>PesoReviewRepositoryIntegrationTests</c>/<c>Migration006…</c>); this store exists so the
/// HTTP/rendered rows exercise the REAL services/pages/endpoints without a database.</remarks>
internal sealed class P2T06ReviewStore : IPesoReviewRepository
{
    private readonly P2T05TestComposition _pesos;
    private readonly List<PesoReviewDecision> _decisions = [];

    public P2T06ReviewStore(P2T05TestComposition pesos)
    {
        _pesos = pesos;
    }

    /// <summary>The recorded decisions (insertion order).</summary>
    public IReadOnlyList<PesoReviewDecision> AllDecisions => _decisions;

    public Task<IReadOnlyList<PesoReviewRow>> GetPendingAsync(
        PendingListQuery query,
        CancellationToken cancellationToken) =>
        BuildRowsAsync(history: false, query, cancellationToken);

    public Task<IReadOnlyList<PesoReviewRow>> GetHistoryAsync(
        HistoryListQuery query,
        CancellationToken cancellationToken) =>
        BuildRowsAsync(history: true, query, cancellationToken);

    private async Task<IReadOnlyList<PesoReviewRow>> BuildRowsAsync(
        bool history,
        object query,
        CancellationToken cancellationToken)
    {
        var rows = new List<PesoReviewRow>();

        // All Pesos of the composition (task-scoped store: one test owns its data).
        foreach (var key in _pesos.SeededPesoIds())
        {
            var peso = await _pesos.GetByIdAsync(key, cancellationToken);
            if (peso is null)
            {
                continue;
            }

            var decisions = _decisions
                .Where(decision => decision.PesoId.Value == key)
                .OrderBy(decision => decision.DecidedAt)
                .ToList();

            var visible = peso.SubmittedAt is not null || decisions.Count > 0;
            if (history && !visible)
            {
                continue;
            }

            if (!history && peso.SubmittedAt is null)
            {
                continue;
            }

            if (!history && peso.Status != PesoStatus.Pendente)
            {
                continue;
            }

            if (history && query is HistoryListQuery historyQuery)
            {
                if (historyQuery.ReviewState is { } state)
                {
                    var matchesState = state is "pendente" or "submetido"
                        ? peso.SubmittedAt is not null && peso.Status == PesoStatus.Pendente
                        : string.Equals(PesoStatusTokens.ToToken(peso.Status), state, StringComparison.Ordinal);
                    if (!matchesState)
                    {
                        continue;
                    }
                }

                if (historyQuery.DecidedFrom is { } from
                    && decisions.Count > 0
                    && decisions[^1].DecidedAt < from)
                {
                    continue;
                }

                if (historyQuery.DecidedTo is { } to
                    && decisions.Count > 0
                    && decisions[^1].DecidedAt > to)
                {
                    continue;
                }

                if (historyQuery.DecidedByUserId is { } by
                    && decisions.All(decision => decision.DecidedByUserId != by))
                {
                    continue;
                }

                if (historyQuery.Decision is { } decisionToken
                    && decisions.All(decision =>
                        !string.Equals(
                            PesoReviewDecisionKindTokens.ToToken(decision.Decision),
                            decisionToken,
                            StringComparison.Ordinal)))
                {
                    continue;
                }
            }

            var traversal = await ResolveTraversalAsync(peso, cancellationToken);
            if (!passesTraversal(traversal, query))
            {
                continue;
            }

            if (query is PendingListQuery pendingQuery)
            {
                if (pendingQuery.SubmittedFrom is { } pendingFrom && peso.SubmittedAt < pendingFrom)
                {
                    continue;
                }

                if (pendingQuery.SubmittedTo is { } pendingTo && peso.SubmittedAt > pendingTo)
                {
                    continue;
                }
            }
            else if (query is HistoryListQuery historyQuery2)
            {
                if (historyQuery2.SubmittedFrom is { } submittedFrom && peso.SubmittedAt < submittedFrom)
                {
                    continue;
                }

                if (historyQuery2.SubmittedTo is { } submittedTo && peso.SubmittedAt > submittedTo)
                {
                    continue;
                }
            }

            rows.Add(new PesoReviewRow(
                peso.PesoId.Value,
                peso.Version,
                PesoStatusTokens.ToToken(peso.Status),
                peso.CmId,
                peso.ToolId,
                traversal.Reference,
                traversal.ProductionNumber,
                traversal.Machine,
                traversal.ToolReference,
                traversal.Processo,
                peso.SubmittedAt,
                peso.SubmittedByUserId,
                peso.Rows.Count,
                decisions.Count > 0,
                decisions.Count == 0
                    ? null
                    : new DecisionSummary(
                        PesoReviewDecisionKindTokens.ToToken(decisions[^1].Decision),
                        decisions[^1].DecidedByUserId,
                        decisions[^1].DecidedAt,
                        decisions[^1].Reason),
                decisions.Count));
        }

        rows = history
            ? rows.OrderByDescending(row => row.SubmittedAt).ThenByDescending(row => row.PesoId).ToList()
            : rows.OrderBy(row => row.SubmittedAt).ThenBy(row => row.PesoId).ToList();

        var (page, pageSize) = query switch
        {
            PendingListQuery pendingQuery => (pendingQuery.Page, pendingQuery.PageSize),
            HistoryListQuery historyQuery => (historyQuery.Page, historyQuery.PageSize),
            _ => (1, 50),
        };

        return rows
            .Skip((Math.Max(page, 1) - 1) * Math.Max(pageSize, 1))
            .Take(Math.Max(pageSize, 1))
            .ToList();
    }

    private static bool passesTraversal((string? Reference, string? ProductionNumber, string? Machine, string? ToolReference, string? Processo) traversal, object query)
    {
        string? reference = null, productionNumber = null, machine = null, processo = null, toolReference = null;
        switch (query)
        {
            case PendingListQuery pendingQuery:
                reference = pendingQuery.Reference;
                productionNumber = pendingQuery.ProductionNumber;
                machine = pendingQuery.Machine;
                processo = pendingQuery.Processo;
                toolReference = pendingQuery.ToolReference;
                break;
            case HistoryListQuery historyQuery:
                reference = historyQuery.Reference;
                productionNumber = historyQuery.ProductionNumber;
                machine = historyQuery.Machine;
                processo = historyQuery.Processo;
                toolReference = historyQuery.ToolReference;
                break;
        }

        if (reference is not null && traversal.Reference != reference)
        {
            return false;
        }

        if (productionNumber is not null && traversal.ProductionNumber != productionNumber)
        {
            return false;
        }

        if (machine is not null && traversal.Machine != machine)
        {
            return false;
        }

        if (processo is not null && traversal.Processo != processo)
        {
            return false;
        }

        if (toolReference is not null && traversal.ToolReference != toolReference)
        {
            return false;
        }

        return true;
    }

    private async Task<(string? Reference, string? ProductionNumber, string? Machine, string? ToolReference, string? Processo)> ResolveTraversalAsync(
        Peso peso,
        CancellationToken cancellationToken)
    {
        if (peso.IsProductionBound && peso.CmId is { } cmId)
        {
            var context = await _pesos.GetCmContextAsync(cmId, cancellationToken);
            if (context is not null)
            {
                var occurrence = await _pesos.JobOnToolStore.ListUsageOccurrencesAsync(
                    context.ToolId,
                    cancellationToken);
                var jobOn = occurrence
                    .Select(item => ((IJobOnRepository)_pesos.JobOnToolStore).GetByIdAsync(item.JobOnId, cancellationToken).GetAwaiter().GetResult())
                    .FirstOrDefault(item => item?.Contexts.Any(entry =>
                        entry.ContextType == ToolContextType.Cm && entry.ContextId == cmId) == true);

                var tool = await _pesos.JobOnToolStore.SearchAsync(
                    new ToolSearchCriteria(null, null, null, null, null, 10),
                    cancellationToken);

                var ficha = tool.FirstOrDefault(entry => entry.ToolId.Value == context.ToolId);

                return (
                    jobOn?.Reference,
                    jobOn?.ProductionNumber,
                    jobOn?.Machine.Value,
                    context.FrozenToolReference,
                    ficha?.Processo is { } processo ? ToolTokens.ToToken(processo) : null);
            }
        }

        if (peso.IsPending && peso.ToolId is { } toolId)
        {
            var tool = await _pesos.JobOnToolStore.SearchAsync(
                new ToolSearchCriteria(null, null, null, null, null, 10),
                cancellationToken);
            var ficha = tool.FirstOrDefault(entry => entry.ToolId.Value == toolId);

            return (null, null, null, ficha?.Reference, ficha?.Processo is { } processo ? ToolTokens.ToToken(processo) : null);
        }

        return (null, null, null, null, null);
    }

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
        // The store mirrors PesoReviewRepository.DecisionAsync: guarded + append-only.
        var peso = _pesos.GetByIdAsync(decision.PesoId.Value, cancellationToken).GetAwaiter().GetResult()
            ?? throw new ConcurrencyConflictException("The Peso no longer exists.");

        if (peso.Version != expectedPesoVersion)
        {
            throw new ConcurrencyConflictException("The Peso was modified concurrently.");
        }

        // Apply the transition to the composition's stored Peso (the same row the create service
        // wrote through the real service stack).
        _pesos.ApplyDecisionTransition(decision.PesoId.Value, decision.Decision);
        _decisions.Add(decision);

        return Task.FromResult(decision);
    }
}