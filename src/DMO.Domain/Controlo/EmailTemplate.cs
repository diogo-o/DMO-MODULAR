namespace DMO.Domain.Controlo;

/// <summary>
/// One email template: subject, body, the applicable document type/context and the Peso email
/// group routing (machine group + associated recipient list).
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §14 and §16.8; P2-T08 email slice (group routing). A template has a
/// unique non-blank <c>name</c>, a non-blank <c>subject</c> and a non-blank <c>body</c> stored
/// verbatim — no placeholder syntax is fixed and no parsing/substitution exists (Q-PLACE).
/// <see cref="DocumentType"/> is <c>null</c> (generic template) or one of the three Beta document
/// output families (Q-DOCTYPE).
/// <para>
/// <see cref="MachineGroup"/> (<c>B</c>/<c>C</c> — the machine groups of
/// <see cref="EmailMachineGroup"/>) and <see cref="EmailListId"/> (the configured recipient list
/// whose addresses are the template's recipients) are the Peso email routing of this slice:
/// <c>machine → group → template → list → recipients</c>. A template with a group but no list, or
/// with a list but no group, is an incomplete routing (the send refuses typed); a template with
/// neither is a plain generic template (no group routing).
/// </para>
/// </remarks>
public sealed record EmailTemplate(
    EmailTemplateId EmailTemplateId,
    string Name,
    string Subject,
    string Body,
    EmailTemplateDocumentType? DocumentType,
    EmailMachineGroup? MachineGroup,
    EmailListId? EmailListId,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);