namespace DMO.Domain.ControloComparacao;

/// <summary>
/// Canonical identity of one Peso Comparação (the optional comparison header tied to one
/// <c>peso_id</c>).
/// </summary>
/// <remarks>
/// <para>
/// Comparação is an <b>optional child of the initial Peso</b>: a <c>peso_id</c> without any
/// <c>comparacao_id</c> is a fully valid normal Peso and every Peso reader stays unchanged. The
/// comparison header exists only when the workflow was actually started and never carries a
/// production identity, a previous-Peso relation or a duplicate Tool identity — the compared CM
/// subjects anchor to the existing canonical <c>cm_contexts</c> rows through
/// <c>cm_id</c>.</para>
/// <para>
/// The conversion from <see cref="Guid"/> is deliberately <b>not</b> implicit and allocation
/// belongs to the backend application layer (the accepted <c>PesoId</c> convention): no client
/// ever supplies or guesses a Comparação identity.</para>
/// </remarks>
public readonly record struct ComparacaoId(Guid Value)
{
    /// <summary>Wraps an existing identifier value.</summary>
    public static ComparacaoId From(Guid value) => new(value);

    /// <summary>Allocates a new Comparação identity (backend-owned allocation, inside the start transaction).</summary>
    public static ComparacaoId New() => new(Guid.NewGuid());

    /// <summary>Whether the identity holds an allocatable value.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>The canonical value.</summary>
    public Guid ToGuid() => Value;

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}