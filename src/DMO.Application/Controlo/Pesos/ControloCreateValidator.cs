namespace DMO.Application.Controlo.Pesos;

/// <summary>
/// The exact machine-readable validation error codes of the Peso area (closed set, §26.2).
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §5.5/§26.2. These are validation <b>inputs</b>, not domain states,
/// and no code outside this set is produced by a validator.
/// </remarks>
public static class ControloCreateValidationErrors
{
    /// <summary>The required production reference was not supplied.</summary>
    public const string ReferenceRequired = "REFERENCE_REQUIRED";

    /// <summary>The required production number was not supplied.</summary>
    public const string ProductionNumberRequired = "PRODUCTION_NUMBER_REQUIRED";

    /// <summary>The supplied machine is not one of the six settled operational machines.</summary>
    public const string MachineUnknown = "MACHINE_UNKNOWN";

    /// <summary>The required water temperature was not supplied.</summary>
    public const string WaterTemperatureRequired = "WATER_TEMPERATURE_REQUIRED";

    /// <summary>The water temperature is outside the canonical supported range 5–35 °C.</summary>
    public const string TemperatureOutOfRange = "TEMPERATURE_OUT_OF_RANGE";

    /// <summary>A valid Peso always keeps at least one measurement row.</summary>
    public const string RowRequired = "ROW_REQUIRED";

    /// <summary>A supplied per-row water weight is not a positive value.</summary>
    public const string RowWeightInvalid = "ROW_WEIGHT_INVALID";

    /// <summary>A supplied row position is not a positive integer.</summary>
    public const string RowPositionInvalid = "ROW_POSITION_INVALID";

    /// <summary>Two rows of one Peso carry the same position.</summary>
    public const string DuplicateRowPosition = "DUPLICATE_ROW_POSITION";

    /// <summary>A supplied volume is negative.</summary>
    public const string VolumeNegative = "VOLUME_NEGATIVE";

    /// <summary>
    /// A computed per-row result (capacity or glass weight) is not strictly positive (C2).
    /// </summary>
    public const string ResultNonPositive = "RESULT_NON_POSITIVE";

    /// <summary>The Peso carrier carries no anchor: a calculation/create needs exactly one.</summary>
    public const string PesoAnchorRequired = "PESO_ANCHOR_REQUIRED";

    /// <summary>The Peso carrier carries two anchors: exactly one of cmId/pendingToolId is valid.</summary>
    public const string PesoAnchorConflict = "PESO_ANCHOR_CONFLICT";

    /// <summary>The supplied production anchor <c>cm_id</c> does not exist.</summary>
    public const string CmContextNotFound = "CM_CONTEXT_NOT_FOUND";

    /// <summary>The supplied Tool does not exist in the canonical registry.</summary>
    public const string ToolNotFound = "TOOL_NOT_FOUND";

    /// <summary>The supplied Tool's type is not CM (only CM Tools anchor a Peso).</summary>
    public const string ToolTypeMismatch = "TOOL_TYPE_MISMATCH";
}

/// <summary>
/// Pure static Peso validator: it runs before any write and returns the exact contracted codes.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §5.5 (closed set) and §7.1/§7.3/§7.4 (create/update/submit share the
/// same closed set; the validator runs before any write — CRE4/AC-C2). The validator performs no
/// database access: anchor existence (CM_CONTEXT_NOT_FOUND/TOOL_NOT_FOUND), Tool type match and the
/// calculation configuration are resolved by the service before any write.
/// <para>
/// The anchor rule: a Peso always anchors to <b>exactly one</b> of <c>cmId</c> (production) or
/// <c>pendingToolId</c> (truthful pending); <c>PESO_ANCHOR_REQUIRED</c> for none and
/// <c>PESO_ANCHOR_CONFLICT</c> for both (Route 8 XOR anchor, AC-R7).</para>
/// <para>
/// A non-positive <c>ExpectedVersion</c> is deliberately not a validation token: it is refused
/// fail-closed by the service as <c>stale-version</c> — never silently accepted, no invented
/// validation code (accepted P2-T04 stance).</para>
/// </remarks>
public static class ControloCreateValidator
{
    /// <summary>Validates the stateless calculation carrier (route 8).</summary>
    public static IReadOnlyList<string> Validate(CalculatePesoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new List<string>();
        ValidateAnchor(errors, command.CmId, command.PendingToolId);
        errors.AddRange(ValidateState(
            command.WaterTemperature,
            command.VolumeMarisaBq,
            command.VolumePuncaoPu,
            command.RowWaterWeightsG));
        return errors;
    }

    /// <summary>Validates a Peso create command before any write.</summary>
    public static IReadOnlyList<string> Validate(CreatePesoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new List<string>();
        ValidateAnchor(errors, command.CmId, command.PendingToolId);
        errors.AddRange(ValidateState(
            command.WaterTemperature,
            command.VolumeMarisaBq,
            command.VolumePuncaoPu,
            command.RowWaterWeightsG));
        return errors;
    }

    /// <summary>Validates a Peso draft-edit command before any write.</summary>
    public static IReadOnlyList<string> Validate(UpdatePesoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ValidateState(
            command.WaterTemperature,
            command.VolumeMarisaBq,
            command.VolumePuncaoPu,
            command.RowWaterWeightsG);
    }

    /// <summary>
    /// Validates a Peso submit command. The command itself carries no input facts: submit
    /// re-validates the <b>persisted</b> state through <see cref="ValidateState"/> (the closed §5.5
    /// set); identity/version anomalies are refused fail-closed by the service as
    /// <c>stale-version</c>/<c>not-found</c> (accepted P2-T04 stance).
    /// </summary>
    public static IReadOnlyList<string> Validate(SubmitPesoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return [];
    }

    /// <summary>Validates an association command before any write.</summary>
    public static IReadOnlyList<string> Validate(AssociatePesoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new List<string>();

        if (command.PesoId == Guid.Empty)
        {
            errors.Add(ControloCreateValidationErrors.PesoAnchorRequired);
        }

        if (command.CmId == Guid.Empty)
        {
            errors.Add(ControloCreateValidationErrors.CmContextNotFound);
        }

        return errors;
    }

    /// <summary>Validates the full current Peso state of a submit (closed §5.5 set).</summary>
    /// <remarks>
    /// The submit command itself carries no input facts: submit re-validates the <b>persisted</b>
    /// state (≥ 1 valid row, temperature range, volumes, anchor — §7.4 step 4) through this shared
    /// closed-set validation.
    /// </remarks>
    public static IReadOnlyList<string> ValidateState(
        decimal waterTemperature,
        decimal? volumeMarisaBq,
        decimal? volumePuncaoPu,
        IReadOnlyList<decimal> rowWaterWeightsG)
    {
        ArgumentNullException.ThrowIfNull(rowWaterWeightsG);

        var errors = new List<string>();

        if (waterTemperature <= 0)
        {
            errors.Add(ControloCreateValidationErrors.WaterTemperatureRequired);
        }
        else if (waterTemperature < 5 || waterTemperature > 35)
        {
            errors.Add(ControloCreateValidationErrors.TemperatureOutOfRange);
        }

        if (volumeMarisaBq < 0)
        {
            errors.Add(ControloCreateValidationErrors.VolumeNegative);
        }

        if (volumePuncaoPu < 0)
        {
            errors.Add(ControloCreateValidationErrors.VolumeNegative);
        }

        if (rowWaterWeightsG.Count == 0)
        {
            errors.Add(ControloCreateValidationErrors.RowRequired);
        }

        foreach (var weight in rowWaterWeightsG)
        {
            if (weight <= 0)
            {
                errors.Add(ControloCreateValidationErrors.RowWeightInvalid);
            }
        }

        errors.AddRange(ValidateRowPositions(DensePositions(rowWaterWeightsG.Count)));

        return errors;
    }

    /// <summary>
    /// Validates a supplied row-position list: positive dense ordinals with no duplicate (the closed
    /// tokens <c>ROW_POSITION_INVALID</c> / <c>DUPLICATE_ROW_POSITION</c>). Service paths derive
    /// dense 1-based positions from the row list order, so these tokens stay a closed-set
    /// reachability proof of the shared validation helper (MES5).
    /// </summary>
    public static IReadOnlyList<string> ValidateRowPositions(IReadOnlyList<int> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var errors = new List<string>();
        var seen = new HashSet<int>();

        foreach (var position in positions)
        {
            if (position < 1)
            {
                errors.Add(ControloCreateValidationErrors.RowPositionInvalid);
            }
            else if (!seen.Add(position))
            {
                errors.Add(ControloCreateValidationErrors.DuplicateRowPosition);
            }
        }

        return errors;
    }

    private static void ValidateAnchor(List<string> errors, Guid? cmId, Guid? pendingToolId)
    {
        var hasCm = cmId is { } cm && cm != Guid.Empty;
        var hasPending = pendingToolId is { } tool && tool != Guid.Empty;

        if (!hasCm && !hasPending)
        {
            errors.Add(ControloCreateValidationErrors.PesoAnchorRequired);
        }
        else if (hasCm && hasPending)
        {
            errors.Add(ControloCreateValidationErrors.PesoAnchorConflict);
        }
    }

    private static IReadOnlyList<int> DensePositions(int count) =>
        Enumerable.Range(1, count).ToArray();
}