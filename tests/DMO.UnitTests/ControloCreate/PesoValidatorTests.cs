using System.Reflection;
using DMO.Application.Controlo.Pesos;

namespace DMO.UnitTests.ControloCreate;

/// <summary>
/// P2-T05 unit proofs of the pure Peso validator: rows MES1, MES2, MES5 and CRE4 of the
/// test-to-acceptance matrix (<c>plans/contracts/P2-T05_CONTROLO_CREATE_CONTRACT.md</c> §26.4),
/// the Route-8 anchor XOR (JRC7/AC-R7 and PID3/AC-P1) and the closed §5.5/§26.2 token set
/// (volumes, submit/associate carrier shapes).
/// </summary>
/// <remarks>
/// The validator runs before any write and returns the exact contracted codes of the closed set
/// (contract §5.5/§26.2). Every assertion here is about that meaning: which refusal is produced
/// and that the refusal is the <b>only</b> one produced for the supplied input.
/// </remarks>
public sealed class PesoValidatorTests
{
    /// <summary>A calculate command over a valid pending anchor with no volumes and one positive row.</summary>
    private static CalculatePesoCommand Calculate(decimal temperature, params decimal[] rows) =>
        new(
            CmId: null,
            PendingToolId: Guid.NewGuid(),
            temperature,
            null,
            null,
            null,
            null,
            rows);

    /// <summary>A create command over a valid pending anchor with one positive row.</summary>
    private static CreatePesoCommand Create(decimal temperature, params decimal[] rows) =>
        new(
            CmId: null,
            PendingToolId: Guid.NewGuid(),
            temperature,
            null,
            null,
            null,
            null,
            rows,
            CreatedByUserId: Guid.NewGuid());

    /// <summary>An update command carrying the full input fact set (the anchor is not editable here).</summary>
    private static UpdatePesoCommand Update(decimal temperature, params decimal[] rows) =>
        new(
            PesoId: Guid.NewGuid(),
            ExpectedVersion: 1,
            temperature,
            null,
            null,
            null,
            null,
            rows);

    /// <summary>
    /// MES1 (AC-M1) — the canonical supported water-temperature range is 5–35 °C inclusive: 4.9 °C
    /// and 35.1 °C are refused with exactly <c>TEMPERATURE_OUT_OF_RANGE</c>, the 5 and 35 boundaries
    /// are accepted, and a non-positive temperature is the separate <c>WATER_TEMPERATURE_REQUIRED</c>
    /// failure — never an out-of-range token.
    /// </summary>
    [Fact]
    public void MES1_OutOfRangeTemperaturesProduceOnlyTheTemperatureToken()
    {
        Assert.Equal(
            new[] { ControloCreateValidationErrors.TemperatureOutOfRange },
            ControloCreateValidator.Validate(Calculate(4.9m, 1m)));
        Assert.Equal(
            new[] { ControloCreateValidationErrors.TemperatureOutOfRange },
            ControloCreateValidator.Validate(Calculate(35.1m, 1m)));

        // Both boundaries are inclusive: the range is [5, 35].
        Assert.Empty(ControloCreateValidator.Validate(Calculate(5m, 1m)));
        Assert.Empty(ControloCreateValidator.Validate(Calculate(35m, 1m)));
        Assert.Empty(ControloCreateValidator.Validate(Calculate(20m, 1m)));

        // A non-positive temperature is the missing-fact token, not an out-of-range token.
        Assert.Equal(
            new[] { ControloCreateValidationErrors.WaterTemperatureRequired },
            ControloCreateValidator.Validate(Calculate(0m, 1m)));
        Assert.Equal(
            new[] { ControloCreateValidationErrors.WaterTemperatureRequired },
            ControloCreateValidator.ValidateState(0m, null, null, [1m]));

        // The same range check is reachable through the shared state validation (the closed set is
        // one set, §5.5).
        Assert.Equal(
            new[] { ControloCreateValidationErrors.TemperatureOutOfRange },
            ControloCreateValidator.ValidateState(4.9m, null, null, [1m]));
        Assert.Empty(ControloCreateValidator.ValidateState(35m, null, null, [1m]));
    }

    /// <summary>
    /// MES2 (AC-M2) — every per-row water weight must be strictly positive: 0 and -5 are refused
    /// with exactly <c>ROW_WEIGHT_INVALID</c> (one token per offending row), and a positive weight is
    /// accepted.
    /// </summary>
    [Fact]
    public void MES2_RowWeightsMustBeStrictlyPositive()
    {
        Assert.Equal(
            new[] { ControloCreateValidationErrors.RowWeightInvalid },
            ControloCreateValidator.Validate(Calculate(20m, 0m)));
        Assert.Equal(
            new[] { ControloCreateValidationErrors.RowWeightInvalid },
            ControloCreateValidator.Validate(Calculate(20m, -5m)));

        // Each offending row contributes its own token, and only that token.
        Assert.Equal(
            new[]
            {
                ControloCreateValidationErrors.RowWeightInvalid,
                ControloCreateValidationErrors.RowWeightInvalid,
            },
            ControloCreateValidator.Validate(Calculate(20m, 0m, -5m)));

        // A strictly positive weight validates clean, including fractional weights.
        Assert.Empty(ControloCreateValidator.Validate(Calculate(20m, 0.0001m)));
        Assert.Empty(ControloCreateValidator.Validate(Calculate(20m, 10m, 20.5m, 30m)));
        Assert.Empty(ControloCreateValidator.ValidateState(20m, null, null, [1m]));
    }

    /// <summary>
    /// MES5 (AC-M5) — a valid Peso keeps at least one measurement row: 0 rows → exactly
    /// <c>ROW_REQUIRED</c>; 1..n rows are accepted; the row-position helper accepts exactly the
    /// positive dense ordinals and reports <c>ROW_POSITION_INVALID</c> for a non-positive position
    /// and <c>DUPLICATE_ROW_POSITION</c> for a repeated one.
    /// </summary>
    [Fact]
    public void MES5_RowCountAndPositionRulesUseTheExactTokens()
    {
        // Variable rows: 1..n accepted, 0 refused with the one row token and nothing else.
        Assert.Equal(
            new[] { ControloCreateValidationErrors.RowRequired },
            ControloCreateValidator.Validate(Calculate(20m)));
        Assert.Equal(
            new[] { ControloCreateValidationErrors.RowRequired },
            ControloCreateValidator.ValidateState(20m, null, null, []));

        Assert.Empty(ControloCreateValidator.Validate(Calculate(20m, 10m)));
        Assert.Empty(ControloCreateValidator.Validate(Calculate(20m, 10m, 20.5m, 30m)));

        // The dense-positions contract: 1-based ordinals only, no gaps implied, no duplicates.
        Assert.Equal(
            new[] { ControloCreateValidationErrors.RowPositionInvalid },
            ControloCreateValidator.ValidateRowPositions([0]));
        Assert.Equal(
            new[] { ControloCreateValidationErrors.RowPositionInvalid },
            ControloCreateValidator.ValidateRowPositions([-1]));
        Assert.Equal(
            new[] { ControloCreateValidationErrors.DuplicateRowPosition },
            ControloCreateValidator.ValidateRowPositions([1, 1]));
        Assert.Equal(
            new[] { ControloCreateValidationErrors.DuplicateRowPosition },
            ControloCreateValidator.ValidateRowPositions([1, 2, 2]));

        Assert.Empty(ControloCreateValidator.ValidateRowPositions([1]));
        Assert.Empty(ControloCreateValidator.ValidateRowPositions([1, 2, 3]));
    }

    /// <summary>
    /// JRC7/PID3 (AC-P1, AC-R7) — the Peso always anchors to exactly one of <c>cm_id</c> or
    /// <c>pending_tool_id</c>: no anchor (including two <c>Guid.Empty</c> values, which count as
    /// absent) → exactly <c>PESO_ANCHOR_REQUIRED</c> on both the calculate and the create carrier;
    /// both anchors → exactly <c>PESO_ANCHOR_CONFLICT</c>; a single real anchor validates clean.
    /// </summary>
    [Fact]
    public void JRC7_CalculateAndCreateRequireExactlyOneAnchorWithTheExactTokens()
    {
        // None supplied and Guid.Empty counts as not supplied on either slot.
        var absent = new CalculatePesoCommand(null, null, 20m, null, null, null, null, [1m]);
        Assert.Equal(
            new[] { ControloCreateValidationErrors.PesoAnchorRequired },
            ControloCreateValidator.Validate(absent));

        var emptyBoth = new CalculatePesoCommand(Guid.Empty, Guid.Empty, 20m, null, null, null, null, [1m]);
        Assert.Equal(
            new[] { ControloCreateValidationErrors.PesoAnchorRequired },
            ControloCreateValidator.Validate(emptyBoth));

        var emptyCm = new CalculatePesoCommand(Guid.Empty, null, 20m, null, null, null, null, [1m]);
        Assert.Equal(
            new[] { ControloCreateValidationErrors.PesoAnchorRequired },
            ControloCreateValidator.Validate(emptyCm));

        var emptyPending = new CalculatePesoCommand(null, Guid.Empty, 20m, null, null, null, null, [1m]);
        Assert.Equal(
            new[] { ControloCreateValidationErrors.PesoAnchorRequired },
            ControloCreateValidator.Validate(emptyPending));

        // Both anchors: the XOR conflict, and nothing else.
        var both = new CalculatePesoCommand(Guid.NewGuid(), Guid.NewGuid(), 20m, null, null, null, null, [1m]);
        Assert.Equal(
            new[] { ControloCreateValidationErrors.PesoAnchorConflict },
            ControloCreateValidator.Validate(both));

        // One real anchor validates clean on each carrier.
        Assert.Empty(ControloCreateValidator.Validate(new CalculatePesoCommand(Guid.NewGuid(), null, 20m, null, null, null, null, [1m])));
        Assert.Empty(ControloCreateValidator.Validate(new CalculatePesoCommand(null, Guid.NewGuid(), 20m, null, null, null, null, [1m])));

        // The create carrier obeys the same XOR anchor rule (route 5; AC-P1).
        Assert.Equal(
            new[] { ControloCreateValidationErrors.PesoAnchorRequired },
            ControloCreateValidator.Validate(new CreatePesoCommand(
                null, null, 20m, null, null, null, null, [1m], Guid.NewGuid())));
        Assert.Equal(
            new[] { ControloCreateValidationErrors.PesoAnchorConflict },
            ControloCreateValidator.Validate(new CreatePesoCommand(
                Guid.NewGuid(), Guid.NewGuid(), 20m, null, null, null, null, [1m], Guid.NewGuid())));
        Assert.Empty(ControloCreateValidator.Validate(new CreatePesoCommand(
            null, Guid.NewGuid(), 20m, null, null, null, null, [1m], Guid.NewGuid())));
    }

    /// <summary>
    /// CRE4 (AC-C2) — calculate, create, update and submit-state validation share ONE closed code
    /// set: the same invalid temperature 41 produces <c>TEMPERATURE_OUT_OF_RANGE</c> from all four
    /// entry points, and 0 rows produces <c>ROW_REQUIRED</c> from all four, before any write.
    /// </summary>
    [Fact]
    public void CRE4_CalculateCreateUpdateAndSubmitStateShareTheClosedValidationSet()
    {
        Assert.Equal(
            new[] { ControloCreateValidationErrors.TemperatureOutOfRange },
            ControloCreateValidator.Validate(Calculate(41m, 1m)));
        Assert.Equal(
            new[] { ControloCreateValidationErrors.TemperatureOutOfRange },
            ControloCreateValidator.Validate(Create(41m, 1m)));
        Assert.Equal(
            new[] { ControloCreateValidationErrors.TemperatureOutOfRange },
            ControloCreateValidator.Validate(Update(41m, 1m)));
        Assert.Equal(
            new[] { ControloCreateValidationErrors.TemperatureOutOfRange },
            ControloCreateValidator.ValidateState(41m, null, null, [1m]));

        Assert.Equal(
            new[] { ControloCreateValidationErrors.RowRequired },
            ControloCreateValidator.Validate(Calculate(20m)));
        Assert.Equal(
            new[] { ControloCreateValidationErrors.RowRequired },
            ControloCreateValidator.Validate(Create(20m)));
        Assert.Equal(
            new[] { ControloCreateValidationErrors.RowRequired },
            ControloCreateValidator.Validate(Update(20m)));
        Assert.Equal(
            new[] { ControloCreateValidationErrors.RowRequired },
            ControloCreateValidator.ValidateState(20m, null, null, []));

        // The validator is a pure static type: no instance state and no persistence dependency, so
        // nothing it runs can write anywhere (AC-C2 "before any write").
        Assert.True(typeof(ControloCreateValidator).IsAbstract && typeof(ControloCreateValidator).IsSealed);
        Assert.Empty(typeof(ControloCreateValidator).GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
            BindingFlags.Instance | BindingFlags.DeclaredOnly));

        var validateOverloads = typeof(ControloCreateValidator)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == nameof(ControloCreateValidator.Validate))
            .ToList();
        Assert.Equal(5, validateOverloads.Count);
        Assert.All(validateOverloads, method => Assert.Equal(typeof(IReadOnlyList<string>), method.ReturnType));
    }

    /// <summary>
    /// CRE4 closed set (AC-C2) — a negative Marisa/BQ or Punção/PU volume is refused with exactly
    /// <c>VOLUME_NEGATIVE</c> on each slot (<c>§5.5</c>/<c>§26.2</c>, part of the shared set); absent
    /// and zero volumes are accepted.
    /// </summary>
    [Fact]
    public void CRE4_VolumesAreRefusedWhenNegativeAndAcceptedWhenAbsent()
    {
        Assert.Equal(
            new[] { ControloCreateValidationErrors.VolumeNegative },
            ControloCreateValidator.ValidateState(20m, -1m, null, [1m]));
        Assert.Equal(
            new[] { ControloCreateValidationErrors.VolumeNegative },
            ControloCreateValidator.ValidateState(20m, null, -1m, [1m]));

        // Each slot reports its own token; both together produce both tokens.
        Assert.Equal(
            new[]
            {
                ControloCreateValidationErrors.VolumeNegative,
                ControloCreateValidationErrors.VolumeNegative,
            },
            ControloCreateValidator.ValidateState(20m, -1m, -1m, [1m]));

        // Absent and zero volumes are the only accepted shapes.
        Assert.Empty(ControloCreateValidator.ValidateState(20m, null, null, [1m]));
        Assert.Empty(ControloCreateValidator.ValidateState(20m, 0m, 0m, [1m]));

        // The same token surfaces through the command carriers of the shared set.
        Assert.Equal(
            new[] { ControloCreateValidationErrors.VolumeNegative },
            ControloCreateValidator.Validate(new CalculatePesoCommand(
                null, Guid.NewGuid(), 20m, -1m, null, null, null, [1m])));
        Assert.Equal(
            new[] { ControloCreateValidationErrors.VolumeNegative },
            ControloCreateValidator.Validate(new CreatePesoCommand(
                null, Guid.NewGuid(), 20m, null, -1m, null, null, [1m], Guid.NewGuid())));
    }

    /// <summary>
    /// CRE7/PID6 validator half (AC-C4, AC-P5) — the submit carrier validates clean because submit
    /// re-validates the <b>persisted</b> state through <c>ValidateState</c>; the association carrier
    /// requires the Peso identity and the explicit candidate <c>cm_id</c> with
    /// <c>PESO_ANCHOR_REQUIRED</c> and <c>CM_CONTEXT_NOT_FOUND</c>.
    /// </summary>
    [Fact]
    public void CRE7_SubmitIsShapeOnlyAndAssociateRequiresItsTwoIdentities()
    {
        // Submit: the command itself carries no input facts, so its validation is empty — the
        // persisted state is re-validated by the service through ValidateState (contract §7.4).
        Assert.Empty(ControloCreateValidator.Validate(
            new SubmitPesoCommand(Guid.NewGuid(), 1, Guid.NewGuid())));

        // Associate: both identities are required, each with its exact token.
        Assert.Empty(ControloCreateValidator.Validate(
            new AssociatePesoCommand(Guid.NewGuid(), Guid.NewGuid(), 1)));

        Assert.Equal(
            new[] { ControloCreateValidationErrors.PesoAnchorRequired },
            ControloCreateValidator.Validate(new AssociatePesoCommand(Guid.Empty, Guid.NewGuid(), 1)));

        Assert.Equal(
            new[] { ControloCreateValidationErrors.CmContextNotFound },
            ControloCreateValidator.Validate(new AssociatePesoCommand(Guid.NewGuid(), Guid.Empty, 1)));

        Assert.Equal(
            new[]
            {
                ControloCreateValidationErrors.PesoAnchorRequired,
                ControloCreateValidationErrors.CmContextNotFound,
            },
            ControloCreateValidator.Validate(new AssociatePesoCommand(Guid.Empty, Guid.Empty, 1)));
    }
}