using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;

namespace DMO.Application.ControloCreate;

/// <summary>
/// The <c>Controlo_Create → Definições</c> settings service: the operational configuration
/// areas (PDF directory, email lists, email templates and the current operational glass
/// densities per processo).
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §12–§14, §20.3; post-closure glass-density correction contract
/// §5.3 (the ONLY delta from the closed baseline: the processo→glass-density operational values
/// live here, routes 18/19; every other Definições decision stays closed).
/// <para>
/// <b>Superseded (Owner clarification P2-T07 §34.3 / P2-T05 §31.3):</b> the repairer register and
/// the machine → repairer assignments are owned by <c>Boquilhas > Definições</c>
/// (<c>IBoquilhasDefinicoesService</c> over the same shared <c>repairers</c> /
/// <c>machine_repairer_assignments</c> tables and repositories). The residual Controlo repairer
/// members were removed (F-06); the shared error tokens remain in
/// <see cref="ControloDefinicoesValidationErrors"/> because the Boquilhas surface consumes them.</para>
/// <para>
/// Settings are site-wide configuration data (Q-SITE): no per-user dimension exists; no setting
/// value ever becomes a canonical identity, a join key, a document identity or production truth
/// (§9.1). Every guarded write is version-checked explicitly and through the active token
/// (Q-CONC, §19): a race surfaces as <c>stale-version</c>, never a silent overwrite and never a
/// generic 500.</para>
/// <para>
/// The PDF-directory check (Q-PDF ruling) executes against the <b>server host</b> filesystem
/// through <see cref="IPdfDirectoryProbe"/> and returns only the typed §12.2 vocabulary; the
/// browser only edits/submits the configuration.</para>
/// <para>
/// The glass-density rows carry the CURRENT operational value per processo (NNPB/PS). A changed
/// density affects only NEW Pesos — the Peso freezes the value actually used
/// (<c>pesos.glass_density_g_cm3</c>) and is never re-resolved or rewritten (Owner rule R3–R6).</para>
/// </remarks>
public sealed class ControloDefinicoesService : IControloDefinicoesService
{
    private readonly IPdfDirectorySettingsRepository _pdfDirectory;
    private readonly IEmailListRepository _emailLists;
    private readonly IEmailTemplateRepository _emailTemplates;
    private readonly IGlassDensitySettingsRepository _glassDensities;
    private readonly IPdfDirectoryProbe _directoryProbe;

    /// <summary>Creates the settings service over its repositories and the directory probe.</summary>
    public ControloDefinicoesService(
        IPdfDirectorySettingsRepository pdfDirectory,
        IEmailListRepository emailLists,
        IEmailTemplateRepository emailTemplates,
        IGlassDensitySettingsRepository glassDensities,
        IPdfDirectoryProbe directoryProbe)
    {
        ArgumentNullException.ThrowIfNull(pdfDirectory);
        ArgumentNullException.ThrowIfNull(emailLists);
        ArgumentNullException.ThrowIfNull(emailTemplates);
        ArgumentNullException.ThrowIfNull(glassDensities);
        ArgumentNullException.ThrowIfNull(directoryProbe);
        _pdfDirectory = pdfDirectory;
        _emailLists = emailLists;
        _emailTemplates = emailTemplates;
        _glassDensities = glassDensities;
        _directoryProbe = directoryProbe;
    }

    // ---------------------------------------------------------------------------------------------
    // PDF directory (§12)
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<SettingsResult> GetPdfDirectoryAsync(CancellationToken cancellationToken)
    {
        var settings = await _pdfDirectory.GetAsync(cancellationToken);

        return new SettingsResult.PdfDirectoryFound(
            settings is null ? null : new PdfDirectoryView(settings.BaseDirectory, settings.Version));
    }

    /// <inheritdoc />
    public async Task<SettingsResult> SetPdfDirectoryAsync(
        SetPdfDirectoryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloDefinicoesValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new SettingsResult.ValidationFailed(errors);
        }

        var settings = new PdfDirectorySettings(
            Guid.NewGuid(),
            command.BaseDirectory.Trim(),
            command.ExpectedVersion ?? 1,
            DateTimeOffset.UtcNow);

        try
        {
            var saved = await _pdfDirectory.SetAsync(settings, cancellationToken);

            return new SettingsResult.PdfDirectorySaved(saved.Version);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(SettingsRefusalReason.StaleVersion, exception.Message);
        }
        catch (ControloPersistenceException exception)
        {
            return Map(exception);
        }
    }

    /// <inheritdoc />
    public async Task<SettingsResult> CheckPdfDirectoryAsync(CancellationToken cancellationToken)
    {
        var settings = await _pdfDirectory.GetAsync(cancellationToken);
        if (settings is null)
        {
            // No row configured yet: the explicit not-configured state, never an error.
            return new SettingsResult.PdfDirectoryCheck(
                new PdfDirectoryCheckResult(PdfDirectoryCheckState.NotConfigured));
        }

        var state = _directoryProbe.Probe(settings.BaseDirectory);

        return new SettingsResult.PdfDirectoryCheck(new PdfDirectoryCheckResult(state));
    }

    // ---------------------------------------------------------------------------------------------
    // Email lists (§13)
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<SettingsResult> ListEmailListsAsync(CancellationToken cancellationToken)
    {
        var lists = await _emailLists.ListAsync(cancellationToken);

        return new SettingsResult.EmailListsFound(
            lists
                .Select(list => new EmailListListItem(
                    list.EmailListId.Value,
                    list.Name,
                    list.Recipients.Count,
                    list.Version))
                .ToList());
    }

    /// <inheritdoc />
    public async Task<SettingsResult> GetEmailListAsync(
        Guid emailListId,
        CancellationToken cancellationToken)
    {
        var list = await _emailLists.GetByIdAsync(emailListId, cancellationToken);

        return list is null
            ? new SettingsResult.NotFound(emailListId)
            : new SettingsResult.EmailListFound(list);
    }

    /// <inheritdoc />
    public async Task<SettingsResult> CreateEmailListAsync(
        CreateEmailListCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloDefinicoesValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new SettingsResult.ValidationFailed(errors);
        }

        var now = DateTimeOffset.UtcNow;
        var emailListId = EmailListId.New();
        var list = new EmailList(emailListId, command.Name.Trim(), Version: 1, now, now, []);
        var recipients = ToRecipients(emailListId, command.Recipients, now);

        try
        {
            var created = await _emailLists.CreatedAsync(list, recipients, cancellationToken);

            return new SettingsResult.EmailListCreated(created.EmailListId.Value, created.Version);
        }
        catch (ControloPersistenceException exception)
        {
            return MapList(exception, command.Name.Trim());
        }
    }

    /// <inheritdoc />
    public async Task<SettingsResult> UpdateEmailListAsync(
        UpdateEmailListCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloDefinicoesValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new SettingsResult.ValidationFailed(errors);
        }

        var persisted = await _emailLists.GetByIdAsync(command.EmailListId, cancellationToken);
        if (persisted is null)
        {
            return new SettingsResult.NotFound(command.EmailListId);
        }

        var stale = AssertVersion(persisted.Version, command.ExpectedVersion, "email list");
        if (stale is not null)
        {
            return stale;
        }

        var now = DateTimeOffset.UtcNow;
        var list = persisted with { Name = command.Name.Trim() };
        var recipients = ToRecipients(persisted.EmailListId, command.Recipients, now);

        try
        {
            var saved = await _emailLists.UpdatedAsync(list, recipients, cancellationToken);

            return new SettingsResult.EmailListUpdated(saved.EmailListId.Value, saved.Version);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(SettingsRefusalReason.StaleVersion, exception.Message);
        }
        catch (ControloPersistenceException exception)
        {
            return MapList(exception, command.Name.Trim());
        }
    }

    /// <inheritdoc />
    public async Task<SettingsResult> DeleteEmailListAsync(
        DeleteEmailListCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloDefinicoesValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new SettingsResult.ValidationFailed(errors);
        }

        var persisted = await _emailLists.GetByIdAsync(command.EmailListId, cancellationToken);
        if (persisted is null)
        {
            return new SettingsResult.NotFound(command.EmailListId);
        }

        var stale = AssertVersion(persisted.Version, command.ExpectedVersion, "email list");
        if (stale is not null)
        {
            return stale;
        }

        try
        {
            await _emailLists.DeletedAsync(command.EmailListId, command.ExpectedVersion, cancellationToken);

            return new SettingsResult.EmailListDeleted(command.EmailListId);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(SettingsRefusalReason.StaleVersion, exception.Message);
        }
        catch (ControloPersistenceException exception)
        {
            return MapList(exception, persisted.Name);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Email templates (§14)
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<SettingsResult> ListEmailTemplatesAsync(CancellationToken cancellationToken) =>
        new SettingsResult.EmailTemplatesFound(await _emailTemplates.ListAsync(cancellationToken));

    /// <inheritdoc />
    public async Task<SettingsResult> GetEmailTemplateAsync(
        Guid emailTemplateId,
        CancellationToken cancellationToken)
    {
        var template = await _emailTemplates.GetByIdAsync(emailTemplateId, cancellationToken);

        return template is null
            ? new SettingsResult.NotFound(emailTemplateId)
            : new SettingsResult.EmailTemplateFound(template);
    }

    /// <inheritdoc />
    public async Task<SettingsResult> CreateEmailTemplateAsync(
        CreateEmailTemplateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloDefinicoesValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new SettingsResult.ValidationFailed(errors);
        }

        var now = DateTimeOffset.UtcNow;
        var template = new EmailTemplate(
            EmailTemplateId.New(),
            command.Name.Trim(),
            command.Subject.Trim(),
            command.Body.Trim(),
            ParseDocumentType(command.DocumentType),
            Version: 1,
            now,
            now);

        try
        {
            var created = await _emailTemplates.CreatedAsync(template, cancellationToken);

            return new SettingsResult.EmailTemplateCreated(created.EmailTemplateId.Value, created.Version);
        }
        catch (ControloPersistenceException exception)
        {
            return MapTemplate(exception, command.Name.Trim());
        }
    }

    /// <inheritdoc />
    public async Task<SettingsResult> UpdateEmailTemplateAsync(
        UpdateEmailTemplateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloDefinicoesValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new SettingsResult.ValidationFailed(errors);
        }

        var persisted = await _emailTemplates.GetByIdAsync(command.EmailTemplateId, cancellationToken);
        if (persisted is null)
        {
            return new SettingsResult.NotFound(command.EmailTemplateId);
        }

        var stale = AssertVersion(persisted.Version, command.ExpectedVersion, "email template");
        if (stale is not null)
        {
            return stale;
        }

        var template = persisted with
        {
            Name = command.Name.Trim(),
            Subject = command.Subject.Trim(),
            Body = command.Body.Trim(),
            DocumentType = ParseDocumentType(command.DocumentType),
        };

        try
        {
            var saved = await _emailTemplates.UpdatedAsync(template, cancellationToken);

            return new SettingsResult.EmailTemplateUpdated(saved.EmailTemplateId.Value, saved.Version);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(SettingsRefusalReason.StaleVersion, exception.Message);
        }
        catch (ControloPersistenceException exception)
        {
            return MapTemplate(exception, command.Name.Trim());
        }
    }

    /// <inheritdoc />
    public async Task<SettingsResult> DeleteEmailTemplateAsync(
        DeleteEmailTemplateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloDefinicoesValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new SettingsResult.ValidationFailed(errors);
        }

        var persisted = await _emailTemplates.GetByIdAsync(command.EmailTemplateId, cancellationToken);
        if (persisted is null)
        {
            return new SettingsResult.NotFound(command.EmailTemplateId);
        }

        var stale = AssertVersion(persisted.Version, command.ExpectedVersion, "email template");
        if (stale is not null)
        {
            return stale;
        }

        try
        {
            await _emailTemplates.DeletedAsync(
                command.EmailTemplateId,
                command.ExpectedVersion,
                cancellationToken);

            return new SettingsResult.EmailTemplateDeleted(command.EmailTemplateId);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(SettingsRefusalReason.StaleVersion, exception.Message);
        }
        catch (ControloPersistenceException exception)
        {
            return MapTemplate(exception, persisted.Name);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Glass densities (correction contract §5.3) — routes 18/19
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<SettingsResult> ListGlassDensitiesAsync(CancellationToken cancellationToken) =>
        new SettingsResult.GlassDensitiesFound(await _glassDensities.ListAsync(cancellationToken));

    /// <inheritdoc />
    public async Task<SettingsResult> UpdateGlassDensityAsync(
        UpdateGlassDensityCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ControloDefinicoesValidator.Validate(command);
        if (errors.Count > 0)
        {
            return new SettingsResult.ValidationFailed(errors);
        }

        var processo = command.Processo.Trim();

        var persisted = await _glassDensities.GetByProcessoAsync(processo, cancellationToken);
        if (persisted is null)
        {
            // Defensive only: both canonical rows are always seeded; never invent a row.
            return new SettingsResult.NotFound(Guid.Empty);
        }

        var stale = AssertVersion(persisted.Version, command.ExpectedVersion, "glass-density setting");
        if (stale is not null)
        {
            return stale;
        }

        var now = DateTimeOffset.UtcNow;
        var updated = persisted with
        {
            DensityGCm3 = command.DensityGCm3,
            UpdatedAt = now,
        };

        try
        {
            var saved = await _glassDensities.UpdatedAsync(updated, cancellationToken);

            // ONLY this processo's row changed; the other processo keeps its value and version.
            return new SettingsResult.GlassDensityUpdated(saved.Processo, saved.DensityGCm3, saved.Version);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Refuse(SettingsRefusalReason.StaleVersion, exception.Message);
        }
        catch (ControloPersistenceException exception)
        {
            // Defensive backstop (the validator pre-empts both CHECK violations): a typed
            // refusal, never a generic 500.
            return Map(exception);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private static EmailTemplateDocumentType? ParseDocumentType(string? documentType) =>
        documentType is null ? null : EmailTemplateDocumentTypeTokens.Parse(documentType.Trim());

    private static IReadOnlyList<EmailRecipient> ToRecipients(
        EmailListId emailListId,
        IReadOnlyList<string> addresses,
        DateTimeOffset now) =>
        addresses
            .Select(address => new EmailRecipient(
                Guid.NewGuid(),
                emailListId,
                address.Trim(),
                now))
            .ToList();

    private static SettingsResult Refuse(
        SettingsRefusalReason reason,
        string message,
        IReadOnlyList<SettingsDependency>? dependencies = null) =>
        new SettingsResult.Refused(reason, message, dependencies);

    private static SettingsResult? AssertVersion(int current, int expected, string what)
    {
        if (current != expected)
        {
            return Refuse(
                SettingsRefusalReason.StaleVersion,
                $"The {what} changed after it was observed (expected version {expected}, " +
                $"current version {current}); nothing was written.");
        }

        return null;
    }

    private static SettingsResult Map(ControloPersistenceException exception) =>
        new SettingsResult.Refused(SettingsRefusalReason.DependencyExists, exception.Message);

    private static SettingsResult MapList(ControloPersistenceException exception, string name) =>
        exception.Reason switch
        {
            ControloPersistenceFailureReason.DuplicateListName => new SettingsResult.DuplicateName(name),

            ControloPersistenceFailureReason.DuplicateAddress => new SettingsResult.ValidationFailed(
                [ControloDefinicoesValidationErrors.AddressInvalid]),

            ControloPersistenceFailureReason.DependencyExists => Refuse(
                SettingsRefusalReason.DependencyExists,
                exception.Message,
                [new SettingsDependency("email-list", exception.Message)]),

            _ => throw exception,
        };

    private static SettingsResult MapTemplate(ControloPersistenceException exception, string name) =>
        exception.Reason switch
        {
            ControloPersistenceFailureReason.DuplicateTemplateName => new SettingsResult.DuplicateName(name),

            ControloPersistenceFailureReason.DependencyExists => Refuse(
                SettingsRefusalReason.DependencyExists,
                exception.Message,
                [new SettingsDependency("email-template", exception.Message)]),

            _ => throw exception,
        };
}