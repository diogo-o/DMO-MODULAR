using DMO.Application.JobOn;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Controlo;
using DMO.Domain.Tools;

namespace DMO.Application.ControloCreate;

/// <summary>
/// The Peso create/measurement/submission service: validation, anchor resolution, the authoritative
/// calculations and the create/edit/submit/associate transactions on ONE <c>peso_id</c>.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §7 (create §7.1, calculate §7.2, edit §7.3, submit §7.4), §5.3
/// (formulas), §20.3; post-closure glass-density correction contract §5.4 (the ONLY calculation
/// delta: the glass density is the CURRENT operational value of the anchor's processo from the
/// Definições settings store <c>glass_density_settings</c> — never a deployment configuration;
/// the former <c>Controlo:Calculation:GlassDensities</c> section lost authority).
/// <para>
/// The backend is the <b>only</b> calculation authority: the formulas are never redefined on the
/// frontend and the persisted per-row results always equal the authoritative formula
/// (§5.1, JRC7 — no preview/persist drift). The service composes the P2-T04 application contracts
/// (<see cref="IJobOnService"/>, <see cref="IToolService"/>) for selection/context reads; the
/// contracted anchor traversal <c>cm_id → tool_id → jobon_id</c> (§6.1/§7.2/§26.3) is resolved
/// through <see cref="IPesoContextRead"/>.</para>
/// <para>
/// Historical immutability (§6, Owner rule R3–R6): stored results are never recomputed from
/// current Tool/Job On state and the frozen glass density is never refreshed by later settings
/// changes; the submit path re-derives and <b>verifies</b> the stored results with the
/// authoritative formula, refusing anything non-positive (C2) or non-derivable — nothing is
/// silently rewritten and nothing is invented. A missing glass-density row (defensive only — both
/// rows are always seeded) fails closed with <c>calculation-configuration-missing</c>.</para>
/// </remarks>
public sealed class ControloCreateService : IControloCreateService
{
    private readonly IPesoRepository _pesos;
    private readonly IPesoContextRead _contextRead;
    private readonly IJobOnService _jobOns;
    private readonly IToolService _tools;
    private readonly IControloCalculationConfiguration _calculation;
    private readonly IGlassDensitySettingsRepository _glassDensities;

    /// <summary>Creates the service over its repositories and the composed P2-T04 contracts.</summary>
    public ControloCreateService(
        IPesoRepository pesos,
        IPesoContextRead contextRead,
        IJobOnService jobOns,
        IToolService tools,
        IControloCalculationConfiguration calculation,
        IGlassDensitySettingsRepository glassDensities)
    {
        ArgumentNullException.ThrowIfNull(pesos);
        ArgumentNullException.ThrowIfNull(contextRead);
        ArgumentNullException.ThrowIfNull(jobOns);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(calculation);
        ArgumentNullException.ThrowIfNull(glassDensities);
        _pesos = pesos;
        _contextRead = contextRead;
        _jobOns = jobOns;
        _tools = tools;
        _calculation = calculation;
        _glassDensities = glassDensities;
    }

    /// <inheritdoc />
    public async Task<PesoResult> CalculateAsync(
        CalculatePesoCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloCreateValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new PesoResult.ValidationFailed(errors);
        }

        var anchor = await ResolveAnchorAsync(command.CmId, command.PendingToolId, cancellationToken);
        if (anchor.ValidationErrors.Count > 0)
        {
            return new PesoResult.ValidationFailed(anchor.ValidationErrors);
        }

        if (await TryResolveCalculationFactsAsync(
                anchor.Processo,
                command.WaterTemperature,
                cancellationToken) is not { } facts)
        {
            return Refuse(
                PesoRefusalReason.CalculationConfigurationMissing,
                "The water density for the entered temperature or the glass-density setting for " +
                "this calculation is not resolvable; no value is invented and nothing is written. " +
                "Configure the calculation and retry.");
        }

        var rows = ComputeRows(
            command.RowWaterWeightsG,
            facts.WaterDensity,
            facts.GlassDensity,
            command.VolumeMarisaBq,
            command.VolumePuncaoPu,
            out var resultErrors);
        if (rows is null)
        {
            return new PesoResult.ValidationFailed(resultErrors);
        }

        return new PesoResult.Calculation(new PesoCalculation(
            command.CmId,
            command.PendingToolId,
            command.WaterTemperature,
            facts.WaterDensity,
            facts.GlassDensity,
            rows));
    }

    /// <inheritdoc />
    public async Task<PesoResult> CreateAsync(
        CreatePesoCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloCreateValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new PesoResult.ValidationFailed(errors);
        }

        var anchor = await ResolveAnchorAsync(command.CmId, command.PendingToolId, cancellationToken);
        if (anchor.ValidationErrors.Count > 0)
        {
            return new PesoResult.ValidationFailed(anchor.ValidationErrors);
        }

        if (await TryResolveCalculationFactsAsync(
                anchor.Processo,
                command.WaterTemperature,
                cancellationToken) is not { } createFacts)
        {
            return Refuse(
                PesoRefusalReason.CalculationConfigurationMissing,
                "The water density for the entered temperature or the glass-density setting for " +
                "this Peso is not resolvable; no value is invented and nothing is written. " +
                "Configure the calculation and retry.");
        }

        var rows = ComputeRows(
            command.RowWaterWeightsG,
            createFacts.WaterDensity,
            createFacts.GlassDensity,
            command.VolumeMarisaBq,
            command.VolumePuncaoPu,
            out var resultErrors);
        if (rows is null)
        {
            return new PesoResult.ValidationFailed(resultErrors);
        }

        var now = DateTimeOffset.UtcNow;
        var pesoId = PesoId.New();

        var peso = new Peso(
            pesoId,
            command.CmId is { } cm && cm != Guid.Empty ? cm : null,
            command.PendingToolId is { } tool && tool != Guid.Empty ? tool : null,
            PesoStatus.Pendente,
            SubmittedAt: null,
            SubmittedByUserId: null,
            command.WaterTemperature,
            command.VolumeMarisaBq,
            command.VolumePuncaoPu,
            createFacts.GlassDensity,
            TrimToNull(command.PreviousProductionEndReference),
            TrimToNull(command.PreviousAverageWeightReference),
            Version: 1,
            command.CreatedByUserId,
            now,
            now,
            Rows: ToRows(pesoId, rows, now));

        try
        {
            var created = await _pesos.CreatedAsync(peso, peso.Rows, cancellationToken);

            return new PesoResult.Created(created.PesoId.Value, created.Version);
        }
        catch (ControloPersistenceException exception)
        {
            return Map(exception);
        }
    }

    /// <inheritdoc />
    public async Task<PesoResult> GetAsync(Guid pesoId, CancellationToken cancellationToken)
    {
        var peso = await _pesos.GetByIdAsync(pesoId, cancellationToken);
        if (peso is null)
        {
            return new PesoResult.NotFound(pesoId);
        }

        return new PesoResult.Found(await BuildSheetAsync(peso, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<PesoResult> UpdateAsync(
        UpdatePesoCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloCreateValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new PesoResult.ValidationFailed(errors);
        }

        var persisted = await _pesos.GetByIdAsync(command.PesoId, cancellationToken);
        if (persisted is null)
        {
            return new PesoResult.NotFound(command.PesoId);
        }

        var stale = AssertCurrentVersion(persisted, command.ExpectedVersion);
        if (stale is not null)
        {
            return stale;
        }

        if (persisted.SubmittedAt is not null)
        {
            return Refuse(
                PesoRefusalReason.AlreadySubmitted,
                "This Peso was already submitted for approval; Create-side edits are closed after " +
                "submission (correction belongs to the approval reopen workflow).");
        }

        var anchor = await ResolveAnchorAsync(persisted.CmId, persisted.ToolId, cancellationToken);
        if (anchor.ValidationErrors.Count > 0)
        {
            return new PesoResult.ValidationFailed(anchor.ValidationErrors);
        }

        // The water density for the edit recompute comes from the current calculation
        // configuration (the authoritative water-temperature table of the application); the
        // glass DENSITY is the Peso's FROZEN density — written once at the first successful
        // calculate/save and never refreshed by later config changes (§6.3.3; a later
        // density-mapping change affects NEW Peso calculations only, MES10).
        if (!_calculation.TryGetWaterDensity(command.WaterTemperature, out var waterDensity))
        {
            return Refuse(
                PesoRefusalReason.CalculationConfigurationMissing,
                "The water density for the entered temperature is not resolvable; no value is " +
                "invented and nothing was written.");
        }

        if (persisted.GlassDensityGCm3 is not { } frozenDensity)
        {
            return Refuse(
                PesoRefusalReason.CalculationConfigurationMissing,
                "This Peso carries no frozen glass density; the draft cannot be recalculated. " +
                "Nothing was written.");
        }

        var rows = ComputeRows(
            command.RowWaterWeightsG,
            waterDensity,
            frozenDensity,
            command.VolumeMarisaBq,
            command.VolumePuncaoPu,
            out var resultErrors);
        if (rows is null)
        {
            return new PesoResult.ValidationFailed(resultErrors);
        }

        var updated = persisted with
        {
            WaterTemperature = command.WaterTemperature,
            VolumeMarisaBq = command.VolumeMarisaBq,
            VolumePuncaoPu = command.VolumePuncaoPu,
            // The frozen glass density is retained verbatim (never refreshed, §6.3.3).
            PreviousProductionEndReference = TrimToNull(command.PreviousProductionEndReference),
            PreviousAverageWeightReference = TrimToNull(command.PreviousAverageWeightReference),
            Rows = ToRows(persisted.PesoId, rows, persisted.CreatedAt),
        };

        try
        {
            var saved = await _pesos.UpdatedAsync(updated, updated.Rows, cancellationToken);

            return new PesoResult.Updated(saved.PesoId.Value, saved.Version);
        }
        catch (ControloPersistenceException exception)
        {
            return Map(exception);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(PesoRefusalReason.StaleVersion, exception.Message);
        }
    }

    /// <inheritdoc />
    public async Task<PesoResult> SubmitAsync(
        SubmitPesoCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var persisted = await _pesos.GetByIdAsync(command.PesoId, cancellationToken);
        if (persisted is null)
        {
            return new PesoResult.NotFound(command.PesoId);
        }

        var stale = AssertCurrentVersion(persisted, command.ExpectedVersion);
        if (stale is not null)
        {
            return stale;
        }

        if (persisted.SubmittedAt is not null)
        {
            return Refuse(
                PesoRefusalReason.AlreadySubmitted,
                "This Peso was already submitted; double submission is refused and no second record " +
                "is created.");
        }

        // Step 4: the full current state is re-validated with the closed §5.5 set.
        var stateErrors = ControloCreateValidator.ValidateState(
            persisted.WaterTemperature,
            persisted.VolumeMarisaBq,
            persisted.VolumePuncaoPu,
            persisted.RowWaterWeightsG);
        if (stateErrors.Count > 0)
        {
            return new PesoResult.ValidationFailed(stateErrors);
        }

        // Step 5: recompute and verify — the stored results must equal the authoritative formula
        // (no preview/persist drift; frozen glass density per §6.3.3, current water-density
        // resolution for the persisted temperature), and every per-row result must be strictly
        // positive (C2). Nothing is silently rewritten.
        var verification = VerifyStoredResults(persisted);
        if (verification is not null)
        {
            return verification;
        }

        var now = DateTimeOffset.UtcNow;
        var submitted = persisted with
        {
            SubmittedAt = now,
            SubmittedByUserId = command.SubmittedByUserId,
        };

        try
        {
            var saved = await _pesos.SubmittedAsync(submitted, cancellationToken);

            return new PesoResult.Submitted(
                saved.PesoId.Value,
                saved.Version,
                saved.SubmittedAt ?? now);
        }
        catch (ControloPersistenceException exception)
        {
            return Map(exception);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(PesoRefusalReason.StaleVersion, exception.Message);
        }
    }

    /// <inheritdoc />
    public async Task<PesoResult> AssociateAsync(
        AssociatePesoCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloCreateValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new PesoResult.ValidationFailed(errors);
        }

        var persisted = await _pesos.GetByIdAsync(command.PesoId, cancellationToken);
        if (persisted is null)
        {
            return new PesoResult.NotFound(command.PesoId);
        }

        var stale = AssertCurrentVersion(persisted, command.ExpectedVersion);
        if (stale is not null)
        {
            return stale;
        }

        if (persisted.SubmittedAt is not null)
        {
            return Refuse(
                PesoRefusalReason.AlreadySubmitted,
                "This Peso was already submitted; Create-side mutations are closed after submission.");
        }

        if (!persisted.IsPending)
        {
            return Refuse(
                PesoRefusalReason.AlreadyAssociated,
                "This Peso is already production-bound; association is offered only while pending.");
        }

        // The target cm_id must exist (CM_CONTEXT_NOT_FOUND) and must resolve to the Peso's anchor
        // tool_id (ASSOCIATION_MISMATCH) — the anchor is the truthful Tool; the application never
        // guesses (§4.4 rules 2–3).
        var target = await _contextRead.GetCmContextAsync(command.CmId, cancellationToken);
        if (target is null)
        {
            return new PesoResult.ValidationFailed([ControloCreateValidationErrors.CmContextNotFound]);
        }

        if (target.ToolId != persisted.ToolId)
        {
            return Refuse(
                PesoRefusalReason.AssociationMismatch,
                "The candidate cm_id does not resolve to this Peso's anchor tool_id; association is " +
                "refused (the application never guesses).");
        }

        var associated = persisted with { CmId = command.CmId, ToolId = null };

        try
        {
            var saved = await _pesos.AssociatedAsync(associated, cancellationToken);

            return new PesoResult.Associated(saved.PesoId.Value, saved.Version, saved.CmId!.Value);
        }
        catch (ControloPersistenceException exception)
        {
            return Map(exception);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(PesoRefusalReason.StaleVersion, exception.Message);
        }
    }

    /// <inheritdoc />
    public async Task<PesoResult> ListAssociationCandidatesAsync(
        Guid toolId,
        CancellationToken cancellationToken)
    {
        if (toolId == Guid.Empty)
        {
            return new PesoResult.ValidationFailed([ControloCreateValidationErrors.ToolNotFound]);
        }

        var result = await _jobOns.ListPesoAssociationCandidatesAsync(toolId, cancellationToken);

        // The Job On read only ever surfaces the accepted candidates carrier for this query.
        return result is JobOnResult.AssociationCandidates(var candidates)
            ? new PesoResult.Candidates(candidates)
            : new PesoResult.ValidationFailed([ControloCreateValidationErrors.ToolNotFound]);
    }

    // ---------------------------------------------------------------------------------------------
    // Anchor resolution and calculations
    // ---------------------------------------------------------------------------------------------

    private sealed record AnchorResolution(IReadOnlyList<string> ValidationErrors, Processo? Processo);

    private async Task<AnchorResolution> ResolveAnchorAsync(
        Guid? cmId,
        Guid? pendingToolId,
        CancellationToken cancellationToken)
    {
        if (pendingToolId is { } toolId && toolId != Guid.Empty)
        {
            var tool = await _tools.GetAsync(toolId, cancellationToken);

            if (tool is not ToolResult.Found(var pendingFicha))
            {
                return new AnchorResolution([ControloCreateValidationErrors.ToolNotFound], null);
            }

            if (pendingFicha.Type != ToolType.Cm)
            {
                return new AnchorResolution([ControloCreateValidationErrors.ToolTypeMismatch], null);
            }

            return new AnchorResolution([], pendingFicha.Processo);
        }

        if (cmId is { } cm && cm != Guid.Empty)
        {
            var context = await _contextRead.GetCmContextAsync(cm, cancellationToken);
            if (context is null)
            {
                return new AnchorResolution([ControloCreateValidationErrors.CmContextNotFound], null);
            }

            var tool = await _tools.GetAsync(context.ToolId, cancellationToken);

            if (tool is not ToolResult.Found(var cmFicha))
            {
                return new AnchorResolution([ControloCreateValidationErrors.ToolNotFound], null);
            }

            if (cmFicha.Type != ToolType.Cm)
            {
                return new AnchorResolution([ControloCreateValidationErrors.ToolTypeMismatch], null);
            }

            return new AnchorResolution([], cmFicha.Processo);
        }

        return new AnchorResolution([ControloCreateValidationErrors.PesoAnchorRequired], null);
    }

    /// <summary>The two resolved calculation facts of one calculation (water + glass densities).</summary>
    private sealed record CalculationFacts(decimal WaterDensity, decimal GlassDensity);

    /// <summary>
    /// Resolves the calculation configuration (Q-CALC; Owner clarification
    /// WATER_TEMPERATURE_TO_WATER_DENSITY_LOOKUP + glass-density correction
    /// GLASS_DENSITY_CONFIGURATION): the WATER DENSITY for the entered temperature (resolved
    /// automatically from the application's authoritative water-temperature table) and the GLASS
    /// density for the anchor's processo — resolved at calculation time from the CURRENT
    /// OPERATIONAL value maintained in <c>Controlo → Definições</c>
    /// (<c>glass_density_settings</c>, per-processo row; never a deployment configuration). The
    /// two lookups are separate facts and are never conflated. An unresolvable fact is
    /// <c>null</c> → the typed <c>calculation-configuration-missing</c> refusal — never an
    /// invented value and never a silent zero (MES9/AC-M9). The operator never enters a water
    /// density or a divisor.
    /// </summary>
    private async Task<CalculationFacts?> TryResolveCalculationFactsAsync(
        Processo? processo,
        decimal waterTemperature,
        CancellationToken cancellationToken)
    {
        if (!_calculation.TryGetWaterDensity(waterTemperature, out var waterDensity))
        {
            return null;
        }

        // The glass density is the CURRENT operational value of the anchor's processo from the
        // Definições settings store (Owner rule): cm_id → tool_id → processo → settings row →
        // density_g_cm3. A missing row is defensive only (both rows are always seeded); it fails
        // closed exactly like the former missing mapping — nothing is invented.
        var token = ToolTokens.ToToken(processo);
        var setting = token is null
            ? null
            : await _glassDensities.GetByProcessoAsync(token, cancellationToken);

        if (setting is null)
        {
            return null;
        }

        return new CalculationFacts(waterDensity, setting.DensityGCm3);
    }

    /// <summary>
    /// Computes every per-row result with the authoritative §5.3 formulas through the SINGLE
    /// shared calculation path (<see cref="PesoRowCalculationRules"/> — the same helper the Peso
    /// Comparação re-measurement path calls; no formula is duplicated). A computed per-row result
    /// that is not strictly positive is refused BEFORE any write with the typed
    /// <c>RESULT_NON_POSITIVE</c> token (C2) — never a 500.
    /// </summary>
    private static IReadOnlyList<PesoRowCalculation>? ComputeRows(
        IReadOnlyList<decimal> waterWeightsG,
        decimal waterDensity,
        decimal glassDensity,
        decimal? volumeMarisaBq,
        decimal? volumePuncaoPu,
        out IReadOnlyList<string> errors) =>
        PesoRowCalculationRules.ComputeRows(
            waterWeightsG,
            waterDensity,
            glassDensity,
            volumeMarisaBq,
            volumePuncaoPu,
            out errors);

    /// <summary>
    /// The submit recompute-and-verify (contract §7.4 step 5): re-derives every per-row result from
    /// the persisted facts with the authoritative formula — the Peso's FROZEN glass density (never
    /// refreshed, §6.3.3) and the current water-density resolution for the persisted temperature —
    /// verifying that every re-derived result is strictly positive (C2) and equals the stored
    /// result (no preview/persist drift). Returns the typed refusal, or <c>null</c> when the stored
    /// facts verify cleanly.
    /// </summary>
    private PesoResult? VerifyStoredResults(Peso peso)
    {
        if (!_calculation.TryGetWaterDensity(peso.WaterTemperature, out var waterDensity))
        {
            return Refuse(
                PesoRefusalReason.CalculationConfigurationMissing,
                "The water density for this Peso's temperature is not resolvable; the stored " +
                "results cannot be verified. Nothing was written.");
        }

        if (peso.GlassDensityGCm3 is not { } frozenDensity)
        {
            return Refuse(
                PesoRefusalReason.CalculationConfigurationMissing,
                "This Peso carries no frozen glass density; the stored results cannot be verified. " +
                "Nothing was written.");
        }

        foreach (var row in peso.Rows.OrderBy(row => row.RowPosition))
        {
            var capacity = decimal.Round(row.WaterWeightG / waterDensity, 4, MidpointRounding.AwayFromZero);
            var glass = decimal.Round(
                (capacity + (peso.VolumeMarisaBq ?? 0) - (peso.VolumePuncaoPu ?? 0)) * frozenDensity,
                4,
                MidpointRounding.AwayFromZero);

            if (capacity <= 0 || glass <= 0)
            {
                return new PesoResult.ValidationFailed([ControloCreateValidationErrors.ResultNonPositive]);
            }

            if (capacity != row.CapacityCm3 || glass != row.GlassWeightG)
            {
                return Refuse(
                    PesoRefusalReason.CalculationConfigurationMissing,
                    "The stored per-row results cannot be re-derived from the current calculation " +
                    "configuration; recalculate the draft before submitting. Nothing was written.");
            }
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Read-model composition (§26.3)
    // ---------------------------------------------------------------------------------------------

    private async Task<PesoSheetReadModel> BuildSheetAsync(
        Peso peso,
        CancellationToken cancellationToken)
    {
        if (peso.IsProductionBound && peso.CmId is { } cmId)
        {
            var context = await _contextRead.GetCmContextAsync(cmId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Peso '{peso.PesoId}' anchors cm_id '{cmId}' which no longer exists in " +
                    "cm_contexts; the persisted anchor cannot be explained truthfully.");

            var ficha = await _jobOns.GetAsync(context.JobOnId, cancellationToken);

            if (ficha is JobOnResult.Ficha(var value))
            {
                var cmContext = value.Contexts.FirstOrDefault(contextEntry =>
                    contextEntry.ContextType == ToolContextType.Cm && contextEntry.ContextId == cmId);

                if (cmContext is not null)
                {
                    var contextProjection = new PesoContextProjection(
                        cmId,
                        context.ToolId,
                        context.FrozenToolType,
                        context.FrozenToolReference,
                        context.FrozenToolLot,
                        cmContext.Tool);

                    var production = new PesoProductionProjection(
                        value.Reference,
                        value.ProductionNumber,
                        value.Machine,
                        value.ProductionDate);

                    return Compose(peso, contextProjection, pending: null, production);
                }
            }
        }

        if (peso.IsPending && peso.ToolId is { } toolId)
        {
            var tool = await _tools.GetAsync(toolId, cancellationToken);

            if (tool is ToolResult.Found(var ficha))
            {
                var pending = new PesoPendingProjection(
                    toolId,
                    new ToolSummaryProjection(
                        ficha.ToolId,
                        ficha.Type,
                        ficha.Reference,
                        ficha.Lot,
                        ficha.Processo,
                        ficha.Quantity,
                        ficha.CompatibleMachines));

                return Compose(peso, context: null, pending, production: null);
            }
        }

        throw new InvalidOperationException(
            $"Peso '{peso.PesoId}' cannot be composed: its anchor is not resolvable through the " +
            "accepted application contracts.");
    }

    private static PesoSheetReadModel Compose(
        Peso peso,
        PesoContextProjection? context,
        PesoPendingProjection? pending,
        PesoProductionProjection? production) =>
        new(
            peso.PesoId.Value,
            peso.Version,
            PesoStatusTokens.ToToken(peso.Status),
            peso.CmId,
            peso.ToolId,
            context,
            pending,
            production,
            peso.CreatedByUserId,
            peso.CreatedAt,
            peso.SubmittedByUserId,
            peso.SubmittedAt,
            peso.WaterTemperature,
            peso.VolumeMarisaBq,
            peso.VolumePuncaoPu,
            peso.GlassDensityGCm3,
            peso.PreviousProductionEndReference,
            peso.PreviousAverageWeightReference,
            peso.Rows
                .OrderBy(row => row.RowPosition)
                .Select(row => new PesoRowReadModel(
                    row.PesoMeasurementRowId.Value,
                    row.RowPosition,
                    row.WaterWeightG,
                    row.CapacityCm3,
                    row.GlassWeightG))
                .ToList());

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private PesoResult? AssertCurrentVersion(Peso peso, int expectedVersion)
    {
        if (peso.Version != expectedVersion)
        {
            return Refuse(
                PesoRefusalReason.StaleVersion,
                $"The Peso changed after it was observed (expected version {expectedVersion}, " +
                $"current version {peso.Version}); nothing was written.");
        }

        return null;
    }

    private static IReadOnlyList<PesoMeasurementRow> ToRows(
        PesoId pesoId,
        IReadOnlyList<PesoRowCalculation> rows,
        DateTimeOffset createdAt) =>
        rows
            .Select(row => new PesoMeasurementRow(
                PesoMeasurementRowId.New(),
                pesoId,
                row.RowPosition,
                row.WaterWeightG,
                row.CapacityCm3,
                row.GlassWeightG,
                createdAt))
            .ToList();

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PesoResult Refuse(PesoRefusalReason reason, string message) =>
        new PesoResult.Refused(reason, message);

    private static PesoResult Map(ControloPersistenceException exception) => exception.Reason switch
    {
        ControloPersistenceFailureReason.ResultNonPositive => new PesoResult.ValidationFailed(
            [ControloCreateValidationErrors.ResultNonPositive]),

        ControloPersistenceFailureReason.CmContextNotFound => new PesoResult.ValidationFailed(
            [ControloCreateValidationErrors.CmContextNotFound]),

        ControloPersistenceFailureReason.ToolNotFound => new PesoResult.ValidationFailed(
            [ControloCreateValidationErrors.ToolNotFound]),

        ControloPersistenceFailureReason.DependencyExists => Refuse(
            PesoRefusalReason.DependencyExists,
            exception.Message),

        _ => throw exception,
    };
}