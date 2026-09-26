namespace DMO.Application.Controlo.Settings;

/// <summary>
/// The single <c>Controlo_Create → Definições</c> settings service contract.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §12–§14 and §20.3; post-closure glass-density correction
/// contract §5.3 (the current operational glass density per processo, routes 18/19).
/// <para>
/// <b>Superseded (Owner clarification P2-T07 §34.3 / P2-T05 §31.3):</b> the repairer register and
/// the machine → repairer assignments moved to <c>Boquilhas > Definições</c>; the residual
/// Controlo repairer members were removed (F-06). The shared error tokens
/// <see cref="ControloDefinicoesValidationErrors.NameRequired"/>,
/// <see cref="ControloDefinicoesValidationErrors.RepairerNotFound"/> and
/// <see cref="ControloDefinicoesValidationErrors.MachineUnknown"/> are still consumed by the
/// Boquilhas surface and remain here.</para>
/// <para>
/// All areas are site-wide database configuration owned by Controlo_Create (Q-SITE); no
/// per-user dimension exists. No setting value ever becomes a canonical identity, a join key, a
/// document identity or production truth (§9.1). The PDF-directory check (Q-PDF) executes on the
/// <b>server host</b> through the filesystem probe abstraction (SET12) and returns only the typed
/// §12.2 vocabulary.</para>
/// </remarks>
public interface IControloDefinicoesService
{
    // PDF directory (§12) — server-host base-directory configuration.
    /// <summary>Reads the configured base directory, or the explicit <c>not-configured</c> state.</summary>
    Task<SettingsResult> GetPdfDirectoryAsync(CancellationToken cancellationToken);

    /// <summary>Configures/changes the base directory (single-row upsert, version-checked).</summary>
    Task<SettingsResult> SetPdfDirectoryAsync(SetPdfDirectoryCommand command, CancellationToken cancellationToken);

    /// <summary>Runs the server-side accessibility check of the configured path (typed result).</summary>
    Task<SettingsResult> CheckPdfDirectoryAsync(CancellationToken cancellationToken);

    // Email lists (§13) — named lists with complete recipient sets.
    /// <summary>Lists the named lists (name + recipient count + version).</summary>
    Task<SettingsResult> ListEmailListsAsync(CancellationToken cancellationToken);

    /// <summary>Reads one named list with its complete recipient set.</summary>
    Task<SettingsResult> GetEmailListAsync(Guid emailListId, CancellationToken cancellationToken);

    /// <summary>Creates a named list with its complete initial recipient set.</summary>
    Task<SettingsResult> CreateEmailListAsync(CreateEmailListCommand command, CancellationToken cancellationToken);

    /// <summary>Replaces the list's name and its complete recipient set atomically.</summary>
    Task<SettingsResult> UpdateEmailListAsync(UpdateEmailListCommand command, CancellationToken cancellationToken);

    /// <summary>Deletes a named list after explicit confirmation.</summary>
    Task<SettingsResult> DeleteEmailListAsync(DeleteEmailListCommand command, CancellationToken cancellationToken);

    // Email templates (§14) — name/subject/body/document type, text verbatim.
    /// <summary>Lists the email templates.</summary>
    Task<SettingsResult> ListEmailTemplatesAsync(CancellationToken cancellationToken);

    /// <summary>Reads one email template.</summary>
    Task<SettingsResult> GetEmailTemplateAsync(Guid emailTemplateId, CancellationToken cancellationToken);

    /// <summary>Creates an email template.</summary>
    Task<SettingsResult> CreateEmailTemplateAsync(CreateEmailTemplateCommand command, CancellationToken cancellationToken);

    /// <summary>Updates an email template, version-guarded.</summary>
    Task<SettingsResult> UpdateEmailTemplateAsync(UpdateEmailTemplateCommand command, CancellationToken cancellationToken);

    /// <summary>Deletes an email template after explicit confirmation.</summary>
    Task<SettingsResult> DeleteEmailTemplateAsync(DeleteEmailTemplateCommand command, CancellationToken cancellationToken);

    // Glass densities (correction contract §5.3) — the current operational value per processo.
    /// <summary>Reads BOTH current operational glass densities (NNPB and PS, with versions).</summary>
    Task<SettingsResult> ListGlassDensitiesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Updates the current operational glass density of ONE canonical processo, version-guarded;
    /// the other processo is never touched (per-processo independence).
    /// </summary>
    Task<SettingsResult> UpdateGlassDensityAsync(UpdateGlassDensityCommand command, CancellationToken cancellationToken);
}