namespace DMO.Web.Pages.Controlo;

/// <summary>
/// The secondary Controlo navigation (Resumo → Peso → Comparação → Pegamentos → Histórico →
/// Definições) shared by every Controlo surface that renders the tab strip: the Resumo landing
/// page (<c>/controlo/resumo</c>) and the Histórico de Pesos page
/// (<c>/controlo/approve/historico</c>). The tab set, order, labels, capability gates and
/// unavailable reasons are defined ONCE here; each surface passes only its own current key, its
/// caller's capability outcome and its hrefs, so the two pages can never drift apart.
/// </summary>
/// <remarks>
/// No route and no permission is invented here: every href is an existing route and the gates
/// mirror the pages' own authorization policies — the create-side surfaces (Resumo, Peso,
/// Definições) require <c>controlo-create</c> and the Histórico surface requires
/// <c>controlo-approve</c>, exactly like the authorization policies on the target pages. A
/// caller lacking a gate sees a truthful unavailable tab with the reason, never a dead link;
/// Comparação and Pegamentos have no backend surface in this build and always render the accepted
/// unavailable state. Context (the selected production identity) travels in the hrefs the caller
/// supplies, over each target route's existing query contract — no new state mechanism.
/// </remarks>
public static class ControloTabs
{
    /// <summary>Tab key of the Resumo landing surface.</summary>
    public static readonly string ResumoKey = ControloTabPresentation.ToKey("Resumo");

    /// <summary>Tab key of the Histórico de Pesos surface.</summary>
    public static readonly string HistoricoKey = ControloTabPresentation.ToKey("Histórico");

    /// <summary>The Resumo landing route.</summary>
    public const string ResumoRoute = "/controlo/resumo";

    /// <summary>The Peso (Controlo Create) route.</summary>
    public const string PesoRoute = "/controlo/create";

    /// <summary>The Histórico de Pesos route.</summary>
    public const string HistoricoRoute = "/controlo/approve/historico";

    /// <summary>The Controlo Definições route.</summary>
    public const string DefinicoesRoute = "/controlo/create/definicoes";

    /// <summary>
    /// Builds the six secondary Controlo tabs in the contracted order (Resumo → Peso → Comparação →
    /// Pegamentos → Histórico → Definições). <paramref name="currentKey"/> marks the surface being
    /// rendered; <paramref name="canCreate"/>/<paramref name="canApprove"/> are the caller's
    /// <c>controlo-create</c>/<c>controlo-approve</c> outcomes and gate the tabs whose targets
    /// carry the matching authorization policy, so a caller without the grant sees a truthful
    /// unavailable tab with the reason instead of a dead link. Comparação and Pegamentos have no
    /// backend surface in this build and always render the accepted unavailable state.
    /// </summary>
    public static IReadOnlyList<ControloTabPresentation> Build(
        string currentKey,
        bool canCreate,
        bool canApprove,
        string resumoHref,
        string pesoHref,
        string historicoHref) =>
    [
        Gated("Resumo", currentKey, canCreate, resumoHref,
            "O Resumo requer a concessão Controlo Create."),
        Gated("Peso", currentKey, canCreate, pesoHref,
            "O Peso requer a concessão Controlo Create."),
        ControloTabPresentation.UnavailableTab(
            "Comparação", "A Comparação ainda não está disponível neste build."),
        ControloTabPresentation.UnavailableTab(
            "Pegamentos", "Os Pegamentos ainda não estão disponíveis neste build."),
        Gated("Histórico", currentKey, canApprove, historicoHref,
            "O Histórico de Pesos requer a concessão Controlo Approve."),
        Gated("Definições", currentKey, canCreate, DefinicoesRoute,
            "As Definições requerem a concessão Controlo Create."),
    ];

    private static ControloTabPresentation Gated(
        string label, string currentKey, bool allowed, string href, string disabledReason) =>
        string.Equals(ControloTabPresentation.ToKey(label), currentKey, StringComparison.Ordinal)
            ? ControloTabPresentation.Current(label, href)
            : allowed
                ? ControloTabPresentation.Link(label, href)
                : ControloTabPresentation.UnavailableTab(label, disabledReason);
}

/// <summary>One secondary Controlo tab: a real route, the current tab, or a stated unavailable area.</summary>
public sealed record ControloTabPresentation(
    string Key,
    string Label,
    string? Href,
    bool IsCurrent,
    string? DisabledReason)
{
    /// <summary>The tab this page renders (the current route, marked current for accessibility).</summary>
    public static ControloTabPresentation Current(string label, string href) =>
        new(ToKey(label), label, href, true, null);

    /// <summary>A tab that navigates to a real existing route.</summary>
    public static ControloTabPresentation Link(string label, string href) =>
        new(ToKey(label), label, href, false, null);

    /// <summary>A tab for an area this build does not serve, with the truthful reason.</summary>
    public static ControloTabPresentation UnavailableTab(string label, string reason) =>
        new(ToKey(label), label, null, false, reason);

    /// <summary>Whether the tab navigates anywhere (only real routes do).</summary>
    public bool IsAvailable => !IsCurrent && !string.IsNullOrWhiteSpace(Href);

    /// <summary>The opaque per-surface hook key of a tab label (diacritics folded).</summary>
    public static string ToKey(string label) => label
        .ToLowerInvariant()
        .Replace("ç", "c", StringComparison.Ordinal)
        .Replace("ã", "a", StringComparison.Ordinal)
        .Replace("õ", "o", StringComparison.Ordinal)
        .Replace("ó", "o", StringComparison.Ordinal)
        .Replace("é", "e", StringComparison.Ordinal)
        .Replace("í", "i", StringComparison.Ordinal);
}
