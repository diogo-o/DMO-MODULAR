using DMO.Application.ControloCreate;
using DMO.Application.Tools;

namespace DMO.Web.Pages.Controlo;

/// <summary>
/// View state for the shared Comparação workspace partial. It is a PRESENTATION slice over the
/// existing <see cref="PesoSheetReadModel"/> — no comparison_id, no duplicated measurements, no
/// duplicated calculations and no independent comparison record. The previous-production side is
/// honest about being unavailable when the real backend does not yet support it.
/// </summary>
public sealed record ComparacaoWorkspaceModel(
    string? Reference,
    string? ProductionNumber,
    string? Machine,
    string? CmReference,
    string? Processo,
    string? Lot,
    IReadOnlyList<PesoRowReadModel> Rows)
{
    /// <summary>Builds the workspace view from the SAME Peso sheet both Create and Approve consume.</summary>
    public static ComparacaoWorkspaceModel? FromPeso(PesoSheetReadModel? sheet)
    {
        if (sheet is null)
        {
            return null;
        }

        var production = sheet.Production;
        var cm = sheet.Context;

        return new ComparacaoWorkspaceModel(
            production?.Reference,
            production?.ProductionNumber,
            production?.Machine,
            cm?.FrozenToolReference,
            cm?.Tool.Processo is { } processo ? ToolTokens.ToToken(processo) : null,
            cm?.FrozenToolLot,
            sheet.Rows);
    }
}
