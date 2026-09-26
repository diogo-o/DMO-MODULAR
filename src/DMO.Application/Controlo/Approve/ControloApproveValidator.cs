namespace DMO.Application.Controlo.Approve;

/// <summary>
/// The P2-T06 validator: pure static validation of the decision commands and the list queries,
/// running before any write and returning the closed §12.2 code set.
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract §9.2/§12.2/§15.1/§15.2.
/// <para>
/// The decision command carriers contain ONLY identity/version/reason — nothing else can be
/// validated (by construction, AC-D1/D2): approve accepts identity+version; reject and reopen
/// additionally require a non-blank reason (<c>REJECT_REASON_REQUIRED</c> /
/// <c>REOPEN_REASON_REQUIRED</c>, with the DB CHECK as the backstop). The list queries accept
/// only well-formed filters: 1-based paging (<c>1 &lt;= PageSize &lt;= 100</c>), the closed
/// review-state vocabulary (<c>submetido | pendente | aprovado | nao_aprovado</c> — a filter, not
/// a status), the closed decision filter vocabulary (<c>aprovado | nao_aprovado | reaberto</c>),
/// and no blank traversal filter — anything else is <c>FILTER_INVALID</c> (never a silent full
/// list).</para>
/// </remarks>
public static class ControloApproveValidator
{
    /// <summary>The closed review-state filter vocabulary (history query; §15.2).</summary>
    public static readonly IReadOnlySet<string> ReviewStateFilterTokens = new HashSet<string>(
        ["submetido", "pendente", "aprovado", "nao_aprovado"],
        StringComparer.Ordinal);

    /// <summary>The closed decision filter vocabulary (history query; §15.2).</summary>
    public static readonly IReadOnlySet<string> DecisionFilterTokens = new HashSet<string>(
        ["aprovado", "nao_aprovado", "reaberto"],
        StringComparer.Ordinal);

    /// <summary>Validates the approve command (identity + version only).</summary>
    public static IReadOnlyList<string> ValidateApprove(ApprovePesoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.PesoId == Guid.Empty)
        {
            return [ControloApproveValidationErrors.FilterInvalid];
        }

        return [];
    }

    /// <summary>Validates the reject command (identity + version + mandatory non-blank reason).</summary>
    public static IReadOnlyList<string> ValidateReject(RejectPesoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.PesoId == Guid.Empty)
        {
            return [ControloApproveValidationErrors.FilterInvalid];
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return [ControloApproveValidationErrors.RejectReasonRequired];
        }

        return [];
    }

    /// <summary>Validates the reopen command (identity + version + mandatory non-blank reason).</summary>
    public static IReadOnlyList<string> ValidateReopen(ReopenPesoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.PesoId == Guid.Empty)
        {
            return [ControloApproveValidationErrors.FilterInvalid];
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return [ControloApproveValidationErrors.ReopenReasonRequired];
        }

        return [];
    }

    /// <summary>Validates the pending list query (paging bounds and filter shape, §15.1).</summary>
    public static IReadOnlyList<string> Validate(PendingListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return ValidatePaging(query.Page, query.PageSize)
            .Concat(ValidateTraversalFilters(query.Reference, query.ProductionNumber, query.Machine, query.Processo, query.ToolReference))
            .ToList();
    }

    /// <summary>Validates the history list query (paging bounds and filter shape, §15.2).</summary>
    public static IReadOnlyList<string> Validate(HistoryListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = ValidatePaging(query.Page, query.PageSize)
            .Concat(ValidateTraversalFilters(query.Reference, query.ProductionNumber, query.Machine, query.Processo, query.ToolReference))
            .ToList();

        if (query.ReviewState is not null && !ReviewStateFilterTokens.Contains(query.ReviewState))
        {
            errors.Add(ControloApproveValidationErrors.FilterInvalid);
        }

        if (query.Decision is not null && !DecisionFilterTokens.Contains(query.Decision))
        {
            errors.Add(ControloApproveValidationErrors.FilterInvalid);
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidatePaging(int page, int pageSize)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
        {
            return [ControloApproveValidationErrors.FilterInvalid];
        }

        return [];
    }

    private static IReadOnlyList<string> ValidateTraversalFilters(params string?[] filters)
    {
        foreach (var filter in filters)
        {
            if (filter is not null && string.IsNullOrWhiteSpace(filter))
            {
                return [ControloApproveValidationErrors.FilterInvalid];
            }
        }

        return [];
    }
}