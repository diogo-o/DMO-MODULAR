namespace DMO.Application.Documents;

/// <summary>
/// The manual Peso PDF email-send command: only the canonical <c>peso_id</c>. No recipient
/// address, no list id and no template value are ever accepted from the client — the recipients
/// and the template are resolved AUTOMATICALLY from the configured group routing
/// (<c>machine → group B/C → template → list → recipients</c>); nothing is hardcoded.
/// </summary>
public sealed record SendPesoPdfCommand(Guid PesoId);

/// <summary>
/// The typed evidence of a successful manual send: the record identity, the EXISTING document
/// that was attached, the configured template used (with the machine group that routed to it)
/// and the resolved recipients (addresses ASC). The evidence is returned to the operator and
/// recorded as status-level application logging — the current persistence model has NO send
/// table and this slice introduces none (a migration would be required; none is authorized),
/// so no send row is written.</summary>
public sealed record PesoPdfSendEvidence(
    Guid PesoId,
    int Version,
    string FileName,
    string TemplateName,
    string MachineGroup,
    IReadOnlyList<string> Recipients,
    DateTimeOffset SentAt);

/// <summary>The typed result of a manual Peso PDF send.</summary>
public abstract record PesoPdfSendResult
{
    /// <summary>The email was handed to the transport with the existing PDF attached.</summary>
    public sealed record Sent(PesoPdfSendEvidence Evidence) : PesoPdfSendResult;

    /// <summary>The Peso does not exist.</summary>
    public sealed record NotFound(Guid PesoId) : PesoPdfSendResult;

    /// <summary>The request itself is invalid (defensive; the endpoint binds only the id).</summary>
    public sealed record ValidationFailed(IReadOnlyList<string> Errors) : PesoPdfSendResult;

    /// <summary>The send was refused with a typed reason — never a generic error and NEVER a
    /// change to the Peso record, the decision or the generated document.</summary>
    public sealed record Refused(PesoPdfSendRefusalReason Reason, string Message) : PesoPdfSendResult;
}

/// <summary>The typed refusal vocabulary of the manual Peso PDF send.</summary>
public enum PesoPdfSendRefusalReason
{
    /// <summary>No base directory is configured in Controlo_Create → Definições.</summary>
    PdfDirectoryNotConfigured,

    /// <summary>The Peso is not decided yet (documents become available after the decision).</summary>
    NotDecided,

    /// <summary>The Peso has no production binding (Job On por associar) — no document exists.</summary>
    ProductionBindingMissing,

    /// <summary>The configured workspace is not usable from the server process.</summary>
    WorkspaceUnavailable,

    /// <summary>A reference/production/machine value cannot form a safe document target.</summary>
    InvalidFileName,

    /// <summary>The deterministic Peso PDF was NOT generated yet — the send attaches the EXISTING
    /// document and NEVER regenerates/recalculates the Peso for sending.</summary>
    PdfNotGenerated,

    /// <summary>The existing PDF could not be read for another infrastructure reason.</summary>
    DocumentReadFailed,

    /// <summary>The machine is outside the two operational groups (B1/B2/B3 → B; C1/C2/C3 → C) —
    /// FAIL CLOSED, nothing is guessed.</summary>
    MachineGroupUnsupported,

    /// <summary>The machine's group has no complete email routing configured (no template of the
    /// group, or a template without its associated recipient list) — informative, never invented.</summary>
    EmailGroupNotConfigured,

    /// <summary>More than one template is configured for the same group and the model fixes no
    /// selection-precedence rule (nothing is invented).</summary>
    EmailTemplateAmbiguous,

    /// <summary>The template's associated recipient list does not exist (defensive; the FK
    /// RESTRICT prevents deletion while referenced).</summary>
    EmailListNotFound,

    /// <summary>The resolved list has no recipients (no address is ever invented).</summary>
    EmailListEmpty,

    /// <summary>The email transport configuration is absent/incomplete — nothing was attempted.</summary>
    EmailTransportNotConfigured,

    /// <summary>The transport failed; the Peso record, the approval and the document are unchanged.</summary>
    EmailSendFailed,
}