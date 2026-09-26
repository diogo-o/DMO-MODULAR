namespace DMO.Application.Controlo.Approve;

/// <summary>
/// The closed per-CM decision vocabulary of the Comparação workflow (contract §17.1, Q-PERCM).
/// </summary>
/// <remarks>
/// Authority: dmo-master <c>CONTROLO.md</c> §10/§15 and the Beta module doc ("per-CM human
/// decisions such as <c>Manter</c> / <c>Colocar de parte</c> where required"): <b>exactly</b>
/// these two values, explicit human decisions only, never implied by the general Peso approval
/// and never selected automatically by warnings/results. This type is the documented constants
/// carrier of the closed vocabulary (D3) — there is <b>no</b> per-CM decision persistence, no
/// table and no route in P2-T06: persistence is enabled only by the Comparação remainder
/// contract (its own gate; §17.3), whose per-CM rows this vocabulary will bind.</remarks>
public static class PerCmDecisionVocabulary
{
    /// <summary>Manter — the CM entry of the Comparação is kept.</summary>
    public const string Manter = "Manter";

    /// <summary>Colocar de parte — the CM entry of the Comparação is set aside.</summary>
    public const string ColocarDeParte = "Colocar de parte";

    /// <summary>The closed two-value vocabulary, in the established order.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Manter,
        ColocarDeParte,
    ];
}