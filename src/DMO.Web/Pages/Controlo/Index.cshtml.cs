using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DMO.Web.Pages.Controlo;

/// <summary>
/// Controlo landing router: <c>GET /controlo</c> never renders a page; it redirects to
/// <c>/controlo/resumo</c>, the Resumo — the landing page of the Controlo area
/// (design <c>current/modulos/controlo/resumo.md</c>: the Resumo is the first/landing tab).
/// </summary>
/// <remarks>
/// The query string is preserved so the design's direct link
/// (<c>?ref=&lt;reference&gt;&amp;production=&lt;production&gt;</c>, or a bare
/// <c>?jobonId=</c>) reaches the Resumo unchanged. No permission is evaluated here: the
/// redirect carries no data and the target page is server-gated by the accepted
/// <c>controlo-create</c> policy, exactly like the account-aware root router leaves gating to
/// the destination.
/// </remarks>
public sealed class ControloIndexModel : PageModel
{
    /// <inheritdoc />
    public IActionResult OnGet() => Redirect($"/controlo/resumo{Request.QueryString.Value}");
}
