using DMO.Domain.Controlo;

namespace DMO.Application.ControloCreate;

/// <summary>
/// The command carriers and closed result set of <c>Controlo_Create → Definições</c>.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §12–§14, §20.3 and §26.1. Every settings write is version-guarded
/// (Q-CONC default) and every setting is site-wide configuration, never a per-user dimension
/// (SET10/AC-F8). Settings are configuration data, not domain records: no setting value ever
/// becomes a canonical identity, a join key, a document identity or production truth (§9.1).
/// <para>
/// <b>Superseded (Owner clarification P2-T07 §34.3 / P2-T05 §31.3):</b> the repairer register and
/// the machine → repairer assignments moved to <c>Boquilhas > Definições</c>; the Controlo
/// repairer/machine-assignment command and result carriers were removed (F-06). The shared error
/// tokens stay in <see cref="ControloDefinicoesValidationErrors"/> because the Boquilhas surface
/// consumes them.</para>
/// </remarks>

// ---------------------------------------------------------------------------------------------
// PDF directory (§12)
// ---------------------------------------------------------------------------------------------

/// <summary>Configures/changes the server-host PDF base directory (single-row upsert, version-checked).</summary>
public sealed record SetPdfDirectoryCommand(string BaseDirectory, int? ExpectedVersion);

/// <summary>One current PDF-directory setting view (null = the explicit <c>not-configured</c> state).</summary>
public sealed record PdfDirectoryView(string BaseDirectory, int Version);

/// <summary>The typed accessibility result states of the server-side directory check (§12.2).</summary>
public enum PdfDirectoryCheckState
{
    /// <summary>No row configured yet — an explicit state, never an error.</summary>
    NotConfigured,

    /// <summary>The path exists, is a directory and is read/write-reachable from the server process.</summary>
    Ok,

    /// <summary>The path does not exist on the server host.</summary>
    DirectoryNotFound,

    /// <summary>The path exists but is not a directory.</summary>
    NotADirectory,

    /// <summary>The directory exists but is not read/write-reachable from the server process account.</summary>
    AccessDenied,

    /// <summary>The configured value is not an absolute path.</summary>
    InvalidPath,

    /// <summary>Any other infrastructure lookup failure (never conflated with directory-not-found).</summary>
    CheckFailed,
}

/// <summary>The typed accessibility result of the server-side check (route 15).</summary>
public sealed record PdfDirectoryCheckResult(PdfDirectoryCheckState State, string? Message = null);

// ---------------------------------------------------------------------------------------------
// Email lists (§13)
// ---------------------------------------------------------------------------------------------

/// <summary>Creates a named list with its complete initial recipient set.</summary>
public sealed record CreateEmailListCommand(string Name, IReadOnlyList<string> Recipients);

/// <summary>
/// Updates a list carrying the <b>complete</b> recipient set (replace-all within one transaction;
/// no partial list state is ever persisted — §18.6, SET4/SET5).
/// </summary>
public sealed record UpdateEmailListCommand(
    Guid EmailListId,
    int ExpectedVersion,
    string Name,
    IReadOnlyList<string> Recipients);

/// <summary>Deletes a named list with explicit confirmation (§13.2).</summary>
public sealed record DeleteEmailListCommand(Guid EmailListId, int ExpectedVersion, bool DeleteConfirmed);

/// <summary>One named list of the list surface (identity, name, recipient count, version).</summary>
public sealed record EmailListListItem(Guid EmailListId, string Name, int RecipientCount, int Version);

// ---------------------------------------------------------------------------------------------
// Glass densities (correction contract §5.3) — routes 18/19
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Updates the CURRENT operational glass density of ONE canonical processo, version-guarded.
/// </summary>
/// <remarks>
/// Owner rule: the Tool does NOT own an editable glass density; <c>Controlo → Definições</c>
/// keeps the current operational value per processo (NNPB/PS only). This write touches exactly
/// that one processo's row — never the other one (MAC2–MAC4 posture). A changed density affects
/// only NEW Pesos; every existing Peso keeps its frozen value.</remarks>
public sealed record UpdateGlassDensityCommand(string Processo, decimal DensityGCm3, int ExpectedVersion);

// ---------------------------------------------------------------------------------------------
// Email templates (§14)
// ---------------------------------------------------------------------------------------------

/// <summary>Creates an email template (name, subject, body, optional document type).</summary>
public sealed record CreateEmailTemplateCommand(
    string Name,
    string Subject,
    string Body,
    string? DocumentType);

/// <summary>Updates an email template, version-guarded.</summary>
public sealed record UpdateEmailTemplateCommand(
    Guid EmailTemplateId,
    int ExpectedVersion,
    string Name,
    string Subject,
    string Body,
    string? DocumentType);

/// <summary>Deletes an email template with explicit confirmation (§14.2).</summary>
public sealed record DeleteEmailTemplateCommand(
    Guid EmailTemplateId,
    int ExpectedVersion,
    bool DeleteConfirmed);

// ---------------------------------------------------------------------------------------------
// Result vocabulary (§26.1)
// ---------------------------------------------------------------------------------------------

/// <summary>One reported settings dependency (kind + human description).</summary>
public sealed record SettingsDependency(string Kind, string Description);

/// <summary>The typed reason of a settings refusal (transport token <c>stale-version</c> /
/// <c>dependency-exists</c>).</summary>
public enum SettingsRefusalReason
{
    /// <summary>The row changed after it was observed; nothing written.</summary>
    StaleVersion,

    /// <summary>Dependent facts exist; nothing deleted.</summary>
    DependencyExists,
}

/// <summary>The closed settings result set (§26.1).</summary>
public abstract record SettingsResult
{
    private SettingsResult()
    {
    }

    /// <summary>The current PDF-directory setting, or the explicit <c>not-configured</c> state.</summary>
    public sealed record PdfDirectoryFound(PdfDirectoryView? View) : SettingsResult;

    /// <summary>The base directory was configured/changed; version incremented.</summary>
    public sealed record PdfDirectorySaved(int Version) : SettingsResult;

    /// <summary>The typed server-side accessibility check result.</summary>
    public sealed record PdfDirectoryCheck(PdfDirectoryCheckResult Check) : SettingsResult;

    /// <summary>Every named email list (identity, name, recipient count, version).</summary>
    public sealed record EmailListsFound(IReadOnlyList<EmailListListItem> Lists) : SettingsResult;

    /// <summary>One named list with its complete recipient set.</summary>
    public sealed record EmailListFound(EmailList List) : SettingsResult;

    /// <summary>A named list was created; the backend allocated <c>email_list_id</c>.</summary>
    public sealed record EmailListCreated(Guid EmailListId, int Version) : SettingsResult;

    /// <summary>The list and its complete recipient set were replaced; version incremented.</summary>
    public sealed record EmailListUpdated(Guid EmailListId, int Version) : SettingsResult;

    /// <summary>The list was deleted after explicit confirmation.</summary>
    public sealed record EmailListDeleted(Guid EmailListId) : SettingsResult;

    /// <summary>Every email template.</summary>
    public sealed record EmailTemplatesFound(IReadOnlyList<EmailTemplate> Templates) : SettingsResult;

    /// <summary>One email template.</summary>
    public sealed record EmailTemplateFound(EmailTemplate Template) : SettingsResult;

    /// <summary>A template was created; the backend allocated <c>email_template_id</c>.</summary>
    public sealed record EmailTemplateCreated(Guid EmailTemplateId, int Version) : SettingsResult;

    /// <summary>The template was updated; version incremented.</summary>
    public sealed record EmailTemplateUpdated(Guid EmailTemplateId, int Version) : SettingsResult;

    /// <summary>The template was deleted after explicit confirmation.</summary>
    public sealed record EmailTemplateDeleted(Guid EmailTemplateId) : SettingsResult;

    /// <summary>Both current operational glass densities (exactly NNPB and PS; correction contract §5.3).</summary>
    public sealed record GlassDensitiesFound(IReadOnlyList<GlassDensitySetting> GlassDensities) : SettingsResult;

    /// <summary>
    /// ONE processo's current operational glass density was updated (its row only; version
    /// incremented; the other processo untouched).
    /// </summary>
    public sealed record GlassDensityUpdated(string Processo, decimal DensityGCm3, int Version) : SettingsResult;

    /// <summary>The exact contracted validation codes; nothing was written.</summary>
    public sealed record ValidationFailed(IReadOnlyList<string> Errors) : SettingsResult;

    /// <summary>The requested settings record does not exist.</summary>
    public sealed record NotFound(Guid Id) : SettingsResult;

    /// <summary>The name is already owned by another list/template (unique-name collision).</summary>
    public sealed record DuplicateName(string Name) : SettingsResult;

    /// <summary>A typed, actionable refusal; nothing was written.</summary>
    public sealed record Refused(
        SettingsRefusalReason Reason,
        string Message,
        IReadOnlyList<SettingsDependency>? Dependencies = null) : SettingsResult;
}