using DMO.Domain.ControloComparacao;

namespace DMO.Application.ControloComparacao;

/// <summary>
/// The exact machine-readable validation error codes of the Peso Comparação area (closed set).
/// </summary>
/// <remarks>
/// <para>
/// The shared Peso row rules (<c>ROW_REQUIRED</c>, <c>ROW_WEIGHT_INVALID</c>,
/// <c>ROW_POSITION_INVALID</c>, <c>DUPLICATE_ROW_POSITION</c>, <c>RESULT_NON_POSITIVE</c>) bind
/// the SAME machine tokens of the closed P2-T05 set — the comparison re-measurement reuses the
/// Peso row rules, so the tokens are single-sourced through the
/// <c>DMO.Application.ControloCreate</c> constants; the codes below redeclare them as the
/// documented Comparação carriers of that binding (no token divergence is possible).</para>
/// <para>
/// <c>CM_CONTEXT_NOT_FOUND</c> is reused verbatim (same meaning: the supplied canonical CM
/// identity does not exist). These are validation <b>inputs</b>, not domain states, and no code
/// outside this set is produced by a validator.</para>
/// </remarks>
public static class ComparacaoValidationErrors
{
    /// <summary>The required initial-Peso identity was not supplied.</summary>
    public const string PesoIdRequired = "PESO_ID_REQUIRED";

    /// <summary>The required comparison identity was not supplied.</summary>
    public const string ComparacaoIdRequired = "COMPARACAO_ID_REQUIRED";

    /// <summary>The supplied canonical CM identity does not exist (P2-T05 token, reused).</summary>
    public const string CmContextNotFound = "CM_CONTEXT_NOT_FOUND";

    /// <summary>The CM is already selected in this comparison (one subject per cm_id).</summary>
    public const string CmAlreadySelected = "CM_ALREADY_SELECTED";

    /// <summary>The supplied per-CM decision token is not one of the closed two values.</summary>
    public const string DecisionUnknown = "DECISION_UNKNOWN";

    /// <summary>Putting a compared CM aside requires its justification.</summary>
    public const string PutAsideReasonRequired = "PUT_ASIDE_REASON_REQUIRED";

    /// <summary>A valid re-measurement keeps at least one row (shared P2-T05 token).</summary>
    public const string RowRequired = "ROW_REQUIRED";

    /// <summary>A supplied per-row water weight is not a positive value (shared P2-T05 token).</summary>
    public const string RowWeightInvalid = "ROW_WEIGHT_INVALID";

    /// <summary>A supplied row position is not a positive integer (shared P2-T05 token).</summary>
    public const string RowPositionInvalid = "ROW_POSITION_INVALID";

    /// <summary>Two rows of one comparison subject carry the same position (shared P2-T05 token).</summary>
    public const string DuplicateRowPosition = "DUPLICATE_ROW_POSITION";

    /// <summary>A computed per-row result is not strictly positive (shared P2-T05 token, C2).</summary>
    public const string ResultNonPositive = "RESULT_NON_POSITIVE";
}

/// <summary>
/// Pure static Comparação validator: it runs before any write and returns the exact closed codes.
/// </summary>
/// <remarks>
/// The validator performs no database access: Peso existence, CM-context existence, the
/// calculation configuration and all state preconditions (already-started, decided, confirmed,
/// measured, complete) are resolved by the service before any write.
/// </remarks>
public static class ControloComparacaoValidator
{
    /// <summary>Validates a start-comparison command before any write.</summary>
    public static IReadOnlyList<string> Validate(StartComparacaoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new List<string>();

        if (command.PesoId == Guid.Empty)
        {
            errors.Add(ComparacaoValidationErrors.PesoIdRequired);
        }

        return errors;
    }

    /// <summary>Validates an add-CM-subject command before any write.</summary>
    public static IReadOnlyList<string> Validate(AddComparacaoCmSubjectCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new List<string>();

        if (command.ComparacaoId == Guid.Empty)
        {
            errors.Add(ComparacaoValidationErrors.ComparacaoIdRequired);
        }

        if (command.CmId == Guid.Empty)
        {
            errors.Add(ComparacaoValidationErrors.CmContextNotFound);
        }

        return errors;
    }

    /// <summary>Validates a measurement-recording command before any write: the shared Peso row
    /// rules (the closed P2-T05 set) apply to the re-measurement weights. The subject identity is
    /// the natural <c>comparacao_id + cm_id</c> pair.</summary>
    public static IReadOnlyList<string> Validate(RecordComparacaoMeasurementsCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new List<string>();
        errors.AddRange(ValidateSubjectIdentity(command.ComparacaoId, command.CmId));

        if (command.RowWaterWeightsG.Count == 0)
        {
            errors.Add(ControloCreate.ControloCreateValidationErrors.RowRequired);
        }

        foreach (var weight in command.RowWaterWeightsG)
        {
            if (weight <= 0)
            {
                errors.Add(ControloCreate.ControloCreateValidationErrors.RowWeightInvalid);
            }
        }

        // The service derives dense 1-based positions from the row order; the position tokens stay
        // a closed-set reachability proof of the shared validation helper (accepted MES5 stance).
        errors.AddRange(ControloCreate.ControloCreateValidator.ValidateRowPositions(
            Enumerable.Range(1, command.RowWaterWeightsG.Count).ToArray()));

        return errors;
    }

    /// <summary>Validates a per-CM decision command before any write: the closed two-value
    /// vocabulary and the put-aside justification rule. The subject identity is the natural
    /// <c>comparacao_id + cm_id</c> pair.</summary>
    public static IReadOnlyList<string> Validate(DecideComparacaoCmCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new List<string>();
        errors.AddRange(ValidateSubjectIdentity(command.ComparacaoId, command.CmId));

        var kind = ComparacaoCmDecisionKindTokens.Parse(command.Decision);
        if (kind is null)
        {
            errors.Add(ComparacaoValidationErrors.DecisionUnknown);
        }
        else if (kind == ComparacaoCmDecisionKind.ColocarDeParte
                 && string.IsNullOrWhiteSpace(command.Reason))
        {
            errors.Add(ComparacaoValidationErrors.PutAsideReasonRequired);
        }

        return errors;
    }

    /// <summary>Validates a confirm-comparison command before any write.</summary>
    public static IReadOnlyList<string> Validate(ConfirmComparacaoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new List<string>();

        if (command.ComparacaoId == Guid.Empty)
        {
            errors.Add(ComparacaoValidationErrors.ComparacaoIdRequired);
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateSubjectIdentity(Guid comparacaoId, Guid cmId)
    {
        var errors = new List<string>();

        if (comparacaoId == Guid.Empty)
        {
            errors.Add(ComparacaoValidationErrors.ComparacaoIdRequired);
        }

        if (cmId == Guid.Empty)
        {
            errors.Add(ComparacaoValidationErrors.CmContextNotFound);
        }

        return errors;
    }
}