namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>email_templates</c> table: one email template.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §16.8; P2-T08 email slice (group routing). <see cref="Name"/> is
/// unique (<c>email_templates_name_key</c>); <see cref="Subject"/> and <see cref="Body"/> are
/// trimmed non-blank; <see cref="Body"/> is stored <b>verbatim</b> — no placeholder parsing or
/// substitution exists (Q-PLACE). <see cref="DocumentType"/> is <c>NULL</c> (generic) or one of
/// <c>peso</c>/<c>pegamentos</c>/<c>resumo</c> (CHECK, Q-DOCTYPE).
/// <para>
/// <see cref="MachineGroup"/> (<c>NULL</c> or <c>B</c>/<c>C</c> — the two machine groups of the
/// Peso email routing) and <see cref="EmailListId"/> (FK RESTRICT → <c>email_lists</c>: the
/// configured list whose addresses are the template's recipients) carry the slice's router:
/// machine → group → template → list → recipients. Both members go together: a template carries
/// either a complete routing (group + list) or none.</para>
/// </remarks>
public sealed class EmailTemplateEntity
{
    /// <summary>Primary key (backend-allocated).</summary>
    public Guid EmailTemplateId { get; set; }

    /// <summary>The unique template name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The template subject.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The template body (verbatim text).</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>The applicable document type token, or <c>null</c> (generic template).</summary>
    public string? DocumentType { get; set; }

    /// <summary>The Peso email machine group token (<c>B</c>/<c>C</c>), or <c>null</c> when the
    /// template carries no group routing.</summary>
    public string? MachineGroup { get; set; }

    /// <summary>The configured recipient list of the group routing, or <c>null</c>.</summary>
    public Guid? EmailListId { get; set; }

    /// <summary>Optimistic-concurrency token.</summary>
    public int Version { get; set; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last-update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}