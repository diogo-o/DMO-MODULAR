namespace DMO.Domain.ControloComparacao;

/// <summary>
/// The closed two-value per-CM decision vocabulary of the Peso Comparação workflow:
/// <c>manter</c> (maintained/accepted) and <c>colocar_de_parte</c> (put aside).
/// </summary>
/// <remarks>
/// <para>
/// Authority: the per-CM decision vocabulary of the Peso Comparação functional authority — a
/// compared CM can be <b>maintained/accepted</b> or <b>put aside</b>; putting a CM aside
/// requires justification; the decision is individual per compared CM and <b>never</b> implied
/// by the general initial-Peso approval and never selected automatically by warnings or
/// results. There is no third value and no automatic decision path.
/// </para>
/// <para>
/// The display labels bind the documented P2-T06 per-CM constants
/// (<c>PerCmDecisionVocabulary</c>: <c>Manter</c> / <c>Colocar de parte</c>) — a pinned unit
/// test asserts the binding; the stored tokens are the exact ASCII lowercase tokens
/// <c>manter</c> / <c>colocar_de_parte</c> (the established Peso token convention: no case
/// folding, no fuzzy matching, ordinal mapping only).</para>
/// </remarks>
public enum ComparacaoCmDecisionKind
{
    /// <summary>Manter — the compared CM is maintained/accepted.</summary>
    Manter,

    /// <summary>Colocar de parte — the compared CM is put aside (justification required).</summary>
    ColocarDeParte,
}

/// <summary>
/// The exact stored ASCII tokens and canonical Portuguese display labels of the per-CM
/// Comparação decisions.
/// </summary>
/// <remarks>
/// The stored tokens are <c>manter</c> and <c>colocar_de_parte</c>; the display labels are
/// <c>Manter</c> and <c>Colocar de parte</c> (the documented P2-T06 per-CM vocabulary — bound by
/// <c>ControloComparacaoVocabularyTests</c>). The mapping is explicit and ordinal: no case
/// folding and no fuzzy matching is invented. The display label is presentation-only and never
/// carries authorization or lifecycle meaning.
/// </remarks>
public static class ComparacaoCmDecisionKindTokens
{
    /// <summary>The stored <c>decision</c> token of a per-CM decision kind.</summary>
    public static string ToToken(ComparacaoCmDecisionKind kind) => kind switch
    {
        ComparacaoCmDecisionKind.Manter => "manter",
        ComparacaoCmDecisionKind.ColocarDeParte => "colocar_de_parte",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown per-CM decision kind."),
    };

    /// <summary>Parses a stored <c>decision</c> token, or <c>null</c> when it is not a per-CM decision.</summary>
    public static ComparacaoCmDecisionKind? Parse(string? token) => token switch
    {
        "manter" => ComparacaoCmDecisionKind.Manter,
        "colocar_de_parte" => ComparacaoCmDecisionKind.ColocarDeParte,
        _ => null,
    };

    /// <summary>The canonical Portuguese display label of a per-CM decision kind.</summary>
    public static string DisplayLabel(ComparacaoCmDecisionKind kind) => kind switch
    {
        ComparacaoCmDecisionKind.Manter => "Manter",
        ComparacaoCmDecisionKind.ColocarDeParte => "Colocar de parte",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown per-CM decision kind."),
    };
}