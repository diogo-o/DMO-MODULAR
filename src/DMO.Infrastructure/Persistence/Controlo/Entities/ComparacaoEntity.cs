namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>comparacoes</c> table: the OPTIONAL comparison header/root tied
/// to exactly one <c>peso_id</c>.
/// </summary>
/// <remarks>
/// <para>
/// The header exists only when the workflow was actually started; a <c>peso_id</c> without a
/// row here is a fully valid normal Peso and every Peso reader stays unchanged. The unique
/// <c>peso_id</c> invariant enforces the single-header rule: at most one comparison per Peso.
/// Deliberately absent: any previous-Peso relation, a production identity, a duplicated Tool
/// identity and any status column — the comparison lifecycle is the nullable confirmation stamp
/// plus the per-CM decision states of its subjects.</para>
/// <para>
/// Writes to this table never touch the <c>pesos</c> row: the initial Peso (measurements,
/// results, status, approval, PDF, history) is the authoritative frozen control and stays
/// untouched.</para>
/// </remarks>
public sealed class ComparacaoEntity
{
    /// <summary>Primary key (backend-allocated; never client-supplied).</summary>
    public Guid ComparacaoId { get; set; }

    /// <summary>The owning initial Peso (FK → pesos RESTRICT; UNIQUE — one header per peso).</summary>
    public Guid PesoId { get; set; }

    /// <summary>Backend-set actor (FK → users RESTRICT).</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>Creation instant (UTC, default <c>now()</c>).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Explicit confirmation stamp; NULL until the comparison is confirmed (CHECK-paired
    /// with <see cref="ConfirmedByUserId"/>).</summary>
    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>Explicit confirmation actor (FK → users RESTRICT); NULL until confirmed.</summary>
    public Guid? ConfirmedByUserId { get; set; }

    /// <summary>Optimistic-concurrency token; guards subject adds and the confirmation stamp.</summary>
    public int Version { get; set; }

    /// <summary>Last-update instant (UTC, default <c>now()</c>).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}