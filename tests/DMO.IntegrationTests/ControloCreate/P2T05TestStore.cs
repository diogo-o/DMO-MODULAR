using DMO.Application.Controlo.Pesos;
using DMO.Application.Controlo.Settings;
using DMO.Application.JobOn;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Controlo;
using DMO.Domain.Tools;
using DMO.IntegrationTests.JobOn;
using DomainPesoId = DMO.Domain.Controlo.PesoId;

namespace DMO.IntegrationTests.ControloCreate;

/// <summary>
/// P2-T05 test composition: the accepted P2-T04 store plus in-memory implementations of every
/// P2-T05 repository contract, the anchor-traversal read, the calculation configuration and the
/// directory probe, with the contracted persistence semantics.
/// </summary>
/// <remarks>
/// <para>
/// The P2-T05 HTTP-class tests exercise the real <see cref="ControloCreateService"/> and
/// <see cref="ControloDefinicoesService"/> over this store (with the REAL Job On/Tool services over
/// the accepted <see cref="P2T04TestStore"/>), so they prove the transport, the policies and the
/// orchestration without requiring a disposable PostgreSQL database. The schema itself is proven
/// separately by the env-gated DB-class tests.</para>
/// <para>
/// Every write is built in locals and committed only at the end, so a forced failure leaves NOTHING
/// behind — the same unit-of-work the real repositories implement with database transactions.
/// <see cref="GetCmContextAsync"/> resolves the contracted anchor traversal through the P2-T04
/// store's own public reads (Tool search → usage occurrences → occurrence ficha), so the mirror is
/// always consistent with the seeded Job On contexts.</para>
/// </remarks>
internal sealed class P2T05TestComposition :
    IPesoRepository,
    IRepairerRepository,
    IMachineRepairerAssignmentRepository,
    IPdfDirectorySettingsRepository,
    IEmailListRepository,
    IEmailTemplateRepository,
    IGlassDensitySettingsRepository,
    IPesoContextRead,
    IProductionResumoRead
{
    /// <summary>The accepted P2-T04 store backing the real Job On/Tool services.</summary>
    public P2T04TestStore JobOnToolStore { get; } = new();

    /// <summary>The fixed WATER-only calculation configuration of the composition
    /// (glass density lives in the settings store, correction contract §5.4).</summary>
    public FixedCalculationConfiguration Calculation { get; } = new();

    /// <summary>The fixed directory-probe verdict of the composition.</summary>
    public FixedPdfDirectoryProbe DirectoryProbe { get; } = new();

    private readonly Dictionary<Guid, Peso> _pesos = [];
    private readonly Dictionary<Guid, IReadOnlyList<PesoMeasurementRow>> _rows = [];
    private readonly Dictionary<Guid, Repairer> _repairers = [];
    private readonly Dictionary<string, MachineRepairerAssignment> _assignments = [];
    private readonly Dictionary<string, GlassDensitySetting> _glassDensities;
    private PdfDirectorySettings? _pdfDirectory;

    public int PesoCount => _pesos.Count;

    public bool FailPesoCreate { get; set; }

    /// <summary>All seeded Peso ids (test-owned arrangement; P2-T06 review-store reads).</summary>
    internal IReadOnlyList<Guid> SeededPesoIds() => _pesos.Keys.ToList();

    /// <summary>
    /// Applies the contracted P2-T06 decision transition on the stored Peso row (same-row status
    /// transition + version bump; reopen restores the pendente draft handoff). Arrangement for the
    /// P2-T06 HTTP tests, mirroring <c>PesoReviewRepository.ApplyTransition</c>.
    /// </summary>
    internal void ApplyDecisionTransition(Guid pesoId, PesoReviewDecisionKind decision)
    {
        if (!_pesos.TryGetValue(pesoId, out var peso))
        {
            return;
        }

        var (status, submittedAt, submittedBy) = decision switch
        {
            PesoReviewDecisionKind.Aprovado => (PesoStatus.Aprovado, peso.SubmittedAt, peso.SubmittedByUserId),
            PesoReviewDecisionKind.NaoAprovado => (PesoStatus.NaoAprovado, peso.SubmittedAt, peso.SubmittedByUserId),
            _ => (PesoStatus.Pendente, (DateTimeOffset?)null, (Guid?)null),
        };

        _pesos[pesoId] = peso with
        {
            Status = status,
            SubmittedAt = submittedAt,
            SubmittedByUserId = submittedBy,
            Version = peso.Version + 1,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Creates the composition with the glass-density store seeded with the provenance-backed
    /// bootstrap values (NNPB 2.4027 / PS 2.4231 g/cm³, version 1 — the sibling-settings
    /// convention), mirroring the correction migration's initial operational state.
    /// </summary>
    public P2T05TestComposition()
    {
        var now = DateTimeOffset.UtcNow;
        _glassDensities = new Dictionary<string, GlassDensitySetting>(StringComparer.Ordinal)
        {
            ["NNPB"] = new("NNPB", 2.4027m, Version: 1, now, now),
            ["PS"] = new("PS", 2.4231m, Version: 1, now, now),
        };
    }

    // ---------------------------------------------------------------- arrangement helpers

    /// <summary>Seeds a canonical Tool and returns it (accepted P2-T04 store arrangement).</summary>
    public DMO.Domain.Tools.Tool SeedTool(
        ToolType type,
        string reference,
        string lot,
        Processo? processo = null,
        int? quantity = null,
        params string[] machines) =>
        JobOnToolStore.SeedTool(type, reference, lot, processo, quantity, machines);

    /// <summary>
    /// Seeds a production occurrence with its CM context and registers the cm_id in the anchor
    /// traversal mirror. Returns the occurrence.
    /// </summary>
    public DMO.Domain.JobOn.JobOn SeedJobOnWithCmContext(
        string reference,
        string productionNumber,
        string machine,
        Guid toolId,
        ToolType toolType,
        string toolReference,
        string toolLot,
        DateOnly? productionDate = null)
    {
        var contextId = Guid.NewGuid();
        var context = new ToolContext(
            ToolContextType.Cm,
            contextId,
            DMO.Domain.JobOn.JobOnId.New(),
            ToolId.From(toolId),
            new ToolContextSnapshot(toolType, toolReference, toolLot));

        return JobOnToolStore.SeedJobOn(
            reference,
            productionNumber,
            machine,
            productionDate,
            copiedFromJobOnId: null,
            contexts: [context]);
    }

    /// <summary>Seeds a repairer directly (test arrangement only).</summary>
    public Repairer SeedRepairer(string name)
    {
        var now = DateTimeOffset.UtcNow;
        var repairer = new Repairer(RepairerId.New(), name, 1, now, now);
        _repairers[repairer.RepairerId.Value] = repairer;

        return repairer;
    }

    /// <summary>Applies a version bump directly, to arrange a stale-version scenario.</summary>
    public void BumpPesoVersion(Guid pesoId)
    {
        var peso = _pesos[pesoId];
        _pesos[pesoId] = peso with { Version = peso.Version + 1 };
    }

    // ----------------------------------------------------------------- IPesoContextRead

    /// <summary>
    /// The contracted anchor traversal resolved through the P2-T04 store's own public reads: for
    /// every canonical Tool, its usage occurrences' fichas are searched for the matching cm_id.
    /// </summary>
    public async Task<CmContextProjection?> GetCmContextAsync(
        Guid cmId,
        CancellationToken cancellationToken)
    {
        var tools = await JobOnToolStore.SearchAsync(
            new ToolSearchCriteria(null, null, null, null, null, 100),
            cancellationToken);

        foreach (var tool in tools)
        {
            var usages = await JobOnToolStore.ListUsageOccurrencesAsync(tool.ToolId.Value, cancellationToken);

            foreach (var usage in usages)
            {
                var occurrence = await ((IJobOnRepository)JobOnToolStore)
                    .GetByIdAsync(usage.JobOnId, cancellationToken);

                if (occurrence is null)
                {
                    continue;
                }

                var cm = occurrence.Contexts.FirstOrDefault(context => context.ContextType == ToolContextType.Cm);
                if (cm is null || cm.ContextId != cmId)
                {
                    continue;
                }

                return new CmContextProjection(
                    cm.ContextId,
                    occurrence.JobOnId.Value,
                    cm.ToolId.Value,
                    ToolTokens.ToToken(cm.Frozen.Type),
                    cm.Frozen.Reference,
                    cm.Frozen.Lot);
            }
        }

        return null;
    }

    // ----------------------------------------------------------------- IProductionResumoRead

    /// <summary>
    /// The Resumo da produção mirror, composed from the P2-T04 store's own public reads (the same
    /// single source of truth as <see cref="GetCmContextAsync"/>): occurrence → CM context → live
    /// Tool facts. Never synthesizes a context and never loads rows the entry does not need.
    /// </summary>
    public async Task<ProductionResumoReadModel?> GetResumoAsync(
        Guid jobOnId,
        CancellationToken cancellationToken)
    {
        var occurrence = await ((IJobOnRepository)JobOnToolStore)
            .GetByIdAsync(jobOnId, cancellationToken);

        if (occurrence is null)
        {
            return null;
        }

        var cm = occurrence.Contexts.FirstOrDefault(context => context.ContextType == ToolContextType.Cm);

        CmResumoProjection? projection = null;

        if (cm is not null)
        {
            var tool = await ((IToolRepository)JobOnToolStore)
                .GetByIdAsync(cm.ToolId.Value, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Context '{cm.ContextId}' references canonical Tool '{cm.ToolId}' which " +
                    "does not exist; the persisted context cannot be explained truthfully.");

            projection = new CmResumoProjection(
                cm.ContextId,
                cm.ToolId.Value,
                ToolTokens.ToToken(cm.Frozen.Type),
                cm.Frozen.Reference,
                cm.Frozen.Lot,
                tool.Reference,
                tool.Lot,
                tool.Processo,
                tool.Quantity);
        }

        return new ProductionResumoReadModel(
            occurrence.JobOnId.Value,
            occurrence.Reference,
            occurrence.ProductionNumber,
            occurrence.Machine.Value,
            occurrence.ProductionDate,
            occurrence.Version,
            projection);
    }

    // ----------------------------------------------------------------- IPesoRepository

    public Task<Peso?> GetByIdAsync(Guid pesoId, CancellationToken cancellationToken) =>
        Task.FromResult(_pesos.TryGetValue(pesoId, out var peso)
            ? peso with { Rows = _rows[pesoId] }
            : null);

    public Task<Peso> CreatedAsync(
        Peso peso,
        IReadOnlyList<PesoMeasurementRow> rows,
        CancellationToken cancellationToken)
    {
        if (FailPesoCreate)
        {
            throw new InvalidOperationException("Forced Peso create failure (test double).");
        }

        _pesos[peso.PesoId.Value] = peso with { Rows = rows };
        _rows[peso.PesoId.Value] = rows;

        return Task.FromResult(_pesos[peso.PesoId.Value]);
    }

    public Task<Peso> UpdatedAsync(
        Peso peso,
        IReadOnlyList<PesoMeasurementRow> rows,
        CancellationToken cancellationToken)
    {
        if (!_pesos.TryGetValue(peso.PesoId.Value, out var persisted))
        {
            throw new ConcurrencyConflictException("The Peso no longer exists.");
        }

        if (persisted.Version != peso.Version)
        {
            throw new ConcurrencyConflictException("The Peso was modified concurrently.");
        }

        var stored = peso with { Version = persisted.Version + 1, Rows = rows };
        _pesos[stored.PesoId.Value] = stored;
        _rows[stored.PesoId.Value] = rows;

        return Task.FromResult(stored);
    }

    public Task<Peso> SubmittedAsync(Peso peso, CancellationToken cancellationToken)
    {
        if (!_pesos.TryGetValue(peso.PesoId.Value, out var persisted))
        {
            throw new ConcurrencyConflictException("The Peso no longer exists.");
        }

        if (persisted.Version != peso.Version)
        {
            throw new ConcurrencyConflictException("The Peso was modified concurrently.");
        }

        var stored = peso with { Version = persisted.Version + 1 };
        _pesos[stored.PesoId.Value] = stored;
        _rows[stored.PesoId.Value] = persisted.Rows;

        return Task.FromResult(stored);
    }

    public Task<Peso> AssociatedAsync(Peso peso, CancellationToken cancellationToken)
    {
        if (!_pesos.TryGetValue(peso.PesoId.Value, out var persisted))
        {
            throw new ConcurrencyConflictException("The Peso no longer exists.");
        }

        if (persisted.Version != peso.Version)
        {
            throw new ConcurrencyConflictException("The Peso was modified concurrently.");
        }

        var stored = peso with { Version = persisted.Version + 1 };
        _pesos[stored.PesoId.Value] = stored;
        _rows[stored.PesoId.Value] = persisted.Rows;

        return Task.FromResult(stored);
    }

    // --------------------------------------------------------------- IRepairerRepository

    Task<Repairer?> IRepairerRepository.GetByIdAsync(Guid repairerId, CancellationToken cancellationToken) =>
        Task.FromResult(_repairers.TryGetValue(repairerId, out var repairer) ? repairer : null);

    Task<IReadOnlyList<Repairer>> IRepairerRepository.ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Repairer>>(
            _repairers.Values
                .OrderBy(repairer => repairer.Name, StringComparer.Ordinal)
                .ToList());

    public Task<Repairer> CreatedAsync(Repairer repairer, CancellationToken cancellationToken)
    {
        _repairers[repairer.RepairerId.Value] = repairer;

        return Task.FromResult(repairer);
    }

    public Task<Repairer> RenamedAsync(Repairer repairer, CancellationToken cancellationToken)
    {
        if (!_repairers.TryGetValue(repairer.RepairerId.Value, out var persisted))
        {
            throw new ConcurrencyConflictException("The repairer no longer exists.");
        }

        if (persisted.Version != repairer.Version)
        {
            throw new ConcurrencyConflictException("The repairer was modified concurrently.");
        }

        var stored = repairer with { Version = persisted.Version + 1 };
        _repairers[stored.RepairerId.Value] = stored;

        return Task.FromResult(stored);
    }

    // ------------------------------------------------- IMachineRepairerAssignmentRepository

    public Task<MachineRepairerAssignment?> GetByMachineAsync(
        string machine,
        CancellationToken cancellationToken) =>
        Task.FromResult(_assignments.TryGetValue(machine, out var assignment) ? assignment : null);

    public Task<IReadOnlyList<MachineRepairerAssignment>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MachineRepairerAssignment>>(_assignments.Values.ToList());

    public Task<MachineRepairerAssignment> SetAsync(
        MachineRepairerAssignment assignment,
        CancellationToken cancellationToken)
    {
        if (_assignments.TryGetValue(assignment.Machine.Value, out var persisted))
        {
            if (persisted.Version != assignment.Version)
            {
                throw new ConcurrencyConflictException("The assignment was modified concurrently.");
            }

            var updated = assignment with { Version = persisted.Version + 1 };
            _assignments[updated.Machine.Value] = updated;

            return Task.FromResult(updated);
        }

        _assignments[assignment.Machine.Value] = assignment;

        return Task.FromResult(assignment);
    }

    public Task ClearedAsync(string machine, int expectedVersion, CancellationToken cancellationToken)
    {
        if (_assignments.TryGetValue(machine, out var persisted))
        {
            if (persisted.Version != expectedVersion)
            {
                throw new ConcurrencyConflictException("The assignment was modified concurrently.");
            }

            _assignments.Remove(machine);
        }

        return Task.CompletedTask;
    }

    // ------------------------------------------------------- IPdfDirectorySettingsRepository

    public Task<PdfDirectorySettings?> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_pdfDirectory);

    public Task<PdfDirectorySettings> SetAsync(
        PdfDirectorySettings settings,
        CancellationToken cancellationToken)
    {
        if (_pdfDirectory is { } persisted)
        {
            if (persisted.Version != settings.Version)
            {
                throw new ConcurrencyConflictException("The PDF directory setting was modified concurrently.");
            }

            var updated = settings with { Version = persisted.Version + 1 };
            _pdfDirectory = updated;

            return Task.FromResult(updated);
        }

        _pdfDirectory = settings;

        return Task.FromResult(settings);
    }

    // ------------------------------------------------------ IGlassDensitySettingsRepository

    public Task<GlassDensitySetting?> GetByProcessoAsync(
        string processo,
        CancellationToken cancellationToken) =>
        Task.FromResult(_glassDensities.TryGetValue(processo, out var setting) ? setting : null);

    Task<IReadOnlyList<GlassDensitySetting>> IGlassDensitySettingsRepository.ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GlassDensitySetting>>(
            _glassDensities.Values
                .OrderBy(setting => setting.Processo, StringComparer.Ordinal)
                .ToList());

    public Task<GlassDensitySetting> UpdatedAsync(
        GlassDensitySetting setting,
        CancellationToken cancellationToken)
    {
        if (!_glassDensities.TryGetValue(setting.Processo, out var persisted))
        {
            throw new ConcurrencyConflictException("The glass-density setting no longer exists.");
        }

        if (persisted.Version != setting.Version)
        {
            throw new ConcurrencyConflictException("The glass-density setting was modified concurrently.");
        }

        var updated = setting with { Version = persisted.Version + 1, UpdatedAt = DateTimeOffset.UtcNow };
        _glassDensities[updated.Processo] = updated;

        return Task.FromResult(updated);
    }

    // ----------------------------------------------------------------- IEmailListRepository

    private readonly Dictionary<Guid, EmailList> _emailLists = [];
    private readonly Dictionary<Guid, IReadOnlyList<EmailRecipient>> _emailRecipients = [];

    Task<EmailList?> IEmailListRepository.GetByIdAsync(Guid emailListId, CancellationToken cancellationToken) =>
        Task.FromResult(_emailLists.TryGetValue(emailListId, out var list)
            ? list with { Recipients = _emailRecipients[emailListId] }
            : null);

    Task<IReadOnlyList<EmailList>> IEmailListRepository.ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmailList>>(
            _emailLists.Values
                .OrderBy(list => list.Name, StringComparer.Ordinal)
                .Select(list => list with { Recipients = _emailRecipients[list.EmailListId.Value] })
                .ToList());

    public Task<EmailList> CreatedAsync(
        EmailList list,
        IReadOnlyList<EmailRecipient> recipients,
        CancellationToken cancellationToken)
    {
        if (_emailLists.Values.Any(existing =>
                string.Equals(existing.Name, list.Name.Trim(), StringComparison.Ordinal)))
        {
            throw new ControloPersistenceException(
                ControloPersistenceFailureReason.DuplicateListName,
                "Another email list already owns this name.");
        }

        var stored = list with { Recipients = recipients };
        _emailLists[stored.EmailListId.Value] = stored;
        _emailRecipients[stored.EmailListId.Value] = recipients;

        return Task.FromResult(stored);
    }

    public Task<EmailList> UpdatedAsync(
        EmailList list,
        IReadOnlyList<EmailRecipient> recipients,
        CancellationToken cancellationToken)
    {
        if (!_emailLists.TryGetValue(list.EmailListId.Value, out var persisted))
        {
            throw new ConcurrencyConflictException("The email list no longer exists.");
        }

        if (persisted.Version != list.Version)
        {
            throw new ConcurrencyConflictException("The email list was modified concurrently.");
        }

        if (_emailLists.Values.Any(existing =>
                existing.EmailListId.Value != list.EmailListId.Value &&
                string.Equals(existing.Name, list.Name.Trim(), StringComparison.Ordinal)))
        {
            throw new ControloPersistenceException(
                ControloPersistenceFailureReason.DuplicateListName,
                "Another email list already owns this name.");
        }

        var stored = list with { Version = persisted.Version + 1, Recipients = recipients };
        _emailLists[stored.EmailListId.Value] = stored;
        _emailRecipients[stored.EmailListId.Value] = recipients;

        return Task.FromResult(stored);
    }

    Task IEmailListRepository.DeletedAsync(Guid emailListId, int expectedVersion, CancellationToken cancellationToken)
    {
        if (!_emailLists.TryGetValue(emailListId, out var persisted))
        {
            throw new ConcurrencyConflictException("The email list no longer exists.");
        }

        if (persisted.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException("The email list was modified concurrently.");
        }

        _emailLists.Remove(emailListId);
        _emailRecipients.Remove(emailListId);

        return Task.CompletedTask;
    }

    // ------------------------------------------------------------ IEmailTemplateRepository

    private readonly Dictionary<Guid, EmailTemplate> _emailTemplates = [];

    Task<EmailTemplate?> IEmailTemplateRepository.GetByIdAsync(Guid emailTemplateId, CancellationToken cancellationToken) =>
        Task.FromResult(_emailTemplates.TryGetValue(emailTemplateId, out var template)
            ? template
            : null);

    Task<IReadOnlyList<EmailTemplate>> IEmailTemplateRepository.ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmailTemplate>>(
            _emailTemplates.Values
                .OrderBy(template => template.Name, StringComparer.Ordinal)
                .ToList());

    public Task<EmailTemplate> CreatedAsync(EmailTemplate template, CancellationToken cancellationToken)
    {
        if (_emailTemplates.Values.Any(existing =>
                string.Equals(existing.Name, template.Name.Trim(), StringComparison.Ordinal)))
        {
            throw new ControloPersistenceException(
                ControloPersistenceFailureReason.DuplicateTemplateName,
                "Another email template already owns this name.");
        }

        _emailTemplates[template.EmailTemplateId.Value] = template;

        return Task.FromResult(template);
    }

    public Task<EmailTemplate> UpdatedAsync(EmailTemplate template, CancellationToken cancellationToken)
    {
        if (!_emailTemplates.TryGetValue(template.EmailTemplateId.Value, out var persisted))
        {
            throw new ConcurrencyConflictException("The email template no longer exists.");
        }

        if (persisted.Version != template.Version)
        {
            throw new ConcurrencyConflictException("The email template was modified concurrently.");
        }

        if (_emailTemplates.Values.Any(existing =>
                existing.EmailTemplateId.Value != template.EmailTemplateId.Value &&
                string.Equals(existing.Name, template.Name.Trim(), StringComparison.Ordinal)))
        {
            throw new ControloPersistenceException(
                ControloPersistenceFailureReason.DuplicateTemplateName,
                "Another email template already owns this name.");
        }

        var stored = template with { Version = persisted.Version + 1 };
        _emailTemplates[stored.EmailTemplateId.Value] = stored;

        return Task.FromResult(stored);
    }

    Task IEmailTemplateRepository.DeletedAsync(Guid emailTemplateId, int expectedVersion, CancellationToken cancellationToken)
    {
        if (!_emailTemplates.TryGetValue(emailTemplateId, out var persisted))
        {
            throw new ConcurrencyConflictException("The email template no longer exists.");
        }

        if (persisted.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException("The email template was modified concurrently.");
        }

        _emailTemplates.Remove(emailTemplateId);

        return Task.CompletedTask;
    }
}

/// <summary>
/// The fixed test WATER-only calculation configuration (Q-CALC; Owner clarification
/// WATER_TEMPERATURE_TO_WATER_DENSITY_LOOKUP): explicit water-density mappings with an
/// empty-variant for the configuration-missing paths. Since the post-closure glass-density
/// correction, the glass density is NOT part of the calculation configuration: it resolves from
/// the composition's glass-density settings store.
/// </summary>
/// <remarks>
/// The fixture densities (20 °C → 0.9982, 25 °C → 0.9971) are ARBITRARY FIXED TEST VALUES used
/// to prove the formula mechanics; they are deliberately NOT the application's authoritative
/// water-temperature table (which is tested separately against
/// <c>ConfigurationCalculationConfiguration.AuthoritativeWaterDensityByCelsius</c> in
/// <c>WaterDensityLookupTests</c>). The implementation applies the same authoritative lookup
/// rule as the production implementation: the entered temperature rounds to the nearest whole
/// degree (<see cref="MidpointRounding.AwayFromZero"/>) before the exact-key lookup.
/// </remarks>
internal sealed class FixedCalculationConfiguration : IControloCalculationConfiguration
{
    private readonly IReadOnlyDictionary<decimal, decimal> _waterDensities;

    public FixedCalculationConfiguration(
        IReadOnlyDictionary<decimal, decimal>? waterDensities = null)
    {
        _waterDensities = waterDensities
            ?? new Dictionary<decimal, decimal> { [20] = 0.9982m, [25] = 0.9971m };
    }

    public bool TryGetWaterDensity(decimal waterTemperature, out decimal waterDensity)
    {
        var key = (int)Math.Round(waterTemperature, MidpointRounding.AwayFromZero);
        return _waterDensities.TryGetValue(key, out waterDensity);
    }

    /// <summary>An empty configuration: every WATER resolution is missing (Q-CALC refusal paths).</summary>
    public static FixedCalculationConfiguration Empty { get; } = new(
        new Dictionary<decimal, decimal>());

    /// <summary>A configuration with an invalid (non-positive) water density (RESULT_NON_POSITIVE
    /// path).</summary>
    public static FixedCalculationConfiguration InvalidWaterDensity { get; } = new(
        new Dictionary<decimal, decimal> { [20] = 0 });
}

/// <summary>
/// The fixed directory-probe verdict of the composition (the real probe is unit-tested over temp
/// directories — SET3/SET12).
/// </summary>
internal sealed class FixedPdfDirectoryProbe : IPdfDirectoryProbe
{
    public PdfDirectoryCheckState Verdict { get; set; } = PdfDirectoryCheckState.Ok;

    public PdfDirectoryCheckState Probe(string absoluteDirectoryPath) => Verdict;
}