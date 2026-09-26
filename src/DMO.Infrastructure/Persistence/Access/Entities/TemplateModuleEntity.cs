namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>template_modules</c> table (Migration 002).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ModuleId"/> is a stable, code-defined Module Registry identity — <b>not</b> a
/// foreign key and never a Module-definition administration table. P1-T04 owns registry and
/// access resolution; P1-T03 persists composition only.
/// </para>
/// <para>
/// No <c>version</c>/timestamps on this table: composition correctness is protected by the
/// Template row's version inside the same transaction.
/// </para>
/// </remarks>
public sealed class TemplateModuleEntity
{
    /// <summary>The owning Template (FK, ON DELETE CASCADE).</summary>
    public Guid TemplateId { get; set; }

    /// <summary>Related Template.</summary>
    public TemplateEntity? Template { get; set; }

    /// <summary>Stable code-defined Module Registry identity.</summary>
    public string ModuleId { get; set; } = string.Empty;

    /// <summary>Deterministic presentation order within the Template (unique per Template).</summary>
    public int PresentationOrder { get; set; }
}