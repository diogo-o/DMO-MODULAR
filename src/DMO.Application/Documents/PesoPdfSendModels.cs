namespace DMO.Application.Documents;

/// <summary>
/// The manual Peso PDF email-send command: the canonical <c>peso_id</c> and an OPTIONAL
/// <c>emailListId</c>. No recipient address and no template value are ever accepted from the
/// client — recipients/template come exclusively from the configured Definições data (the
/// operator selects the applicable configured list only when configuration does not determine
/// it; nothing is hardcoded).
/// </summary>
public sealed record SendPesoPdfCommand(Guid PesoId, Guid? EmailListId);

/// <summary>
/// The typed evidence of a successful manual send: the record identity, the EXISTING document
/// that was attached, the configured template used and the resolved recipients (addresses ASC).
/// The evidence is returned to the operator and recorded as status-level application logging —
/// the current persistence model has NO send table and this slice introduces none (a migration
/// would be required; none is authorized), so no send row is written.</summary>
public sealed record PesoPdfSendEvidence(
    Guid PesoId,
    int Version,
    string FileName,
    string TemplateName,
    IReadOnlyList<string> Recipients,
    DateTimeOffset SentAt);

/// <summary>The typed result of a manual Peso PDF send.</summary>
public abstract record PesoPdfSendResult
{
    /// <summary>The email was handed to the transport with the existing PDF attached.</summary>
    public sealed record Sent(PesoPdfSendEvidence Evidence) : PesoPdfSendResult;

    /// <summary>The Peso does not exist.</summary>
    public sealed record NotFound(Guid PesoId) : PesoPdfSendResult;

    /// <summary>The request itself is invalid (defensive; the endpoint binds an id + optional list id).</summary>
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

    /// <summary>No email template applies to the Peso output (no <c>peso</c> and no generic template).</summary>
    EmailTemplateNotConfigured,

    /// <summary>More than one template applies with the same precedence and the current model fixes
    /// no selection-precedence rule (Q-DOCTYPE applicability: <c>peso</c> beats generic; beyond
    /// that nothing is invented).</summary>
    EmailTemplateAmbiguous,

    /// <summary>Zero email lists are configured and none was selected.</summary>
    EmailListNotConfigured,

    /// <summary>More than one list is configured and none was selected — configuration does not
    /// determine the recipients; the operator selects the applicable configured list.</summary>
    EmailListSelectionRequired,

    /// <summary>The selected list id does not exist.</summary>
    EmailListNotFound,

    /// <summary>The resolved list has no recipients (no address is ever invented).</summary>
    EmailListEmpty,

    /// <summary>The email transport configuration is absent/incomplete — nothing was attempted.</summary>
    EmailTransportNotConfigured,

    /// <summary>The transport failed; the Peso record, the approval and the document are unchanged.</summary>
    EmailSendFailed,
}