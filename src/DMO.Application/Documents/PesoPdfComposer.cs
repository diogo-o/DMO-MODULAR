using DMO.Application.ControloCreate;
using DMO.Application.Tools;
using DMO.Domain.Controlo;

namespace DMO.Application.Documents;

/// <summary>
/// The pure composition of the Peso PDF document model from the SHARED P2-T05 read model — the
/// same <c>PesoSheetReadModel</c> that Create and Approve render (no parallel read, no parallel
/// calculation path).
/// </summary>
/// <remarks>
/// Every fact printed by the document is a member of the supplied read model: the identification
/// block follows the fixed reading order Referência → Produção → Máquina → Data → CM → Processo →
/// Estado → Lote; <b>Boquilha</b> is never shown in the main identification block (no nozzle fact
/// exists in the read model and none is invented); the lot is the frozen CM lot rendered verbatim
/// as a plain value (no <c>L</c> prefix); the per-row results are the FROZEN stored facts passed
/// through with zero recomputation. The composer is read-only and mutation-free; the service
/// guarantees the preconditions (decided + production-bound) before composing.
/// </remarks>
public static class PesoPdfComposer
{
    /// <summary>
    /// Composes the document model from the shared read model. Requires a production-bound decided
    /// sheet (<see cref="PesoSheetReadModel.Production"/> and
    /// <see cref="PesoSheetReadModel.Context"/> present); the service enforces those before
    /// composing, so a violation here is an internal invariant failure, never an invented fact.
    /// </summary>
    public static PesoPdfDocumentModel Compose(PesoSheetReadModel sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var production = sheet.Production
            ?? throw new InvalidOperationException(
                $"Peso '{sheet.PesoId}' has no production binding; a Peso PDF target cannot be " +
                "composed (invariant: the service refuses before composing).");

        var context = sheet.Context
            ?? throw new InvalidOperationException(
                $"Peso '{sheet.PesoId}' has no CM context; the identification block cannot be " +
                "composed (invariant: the service refuses before composing).");

        var status = PesoStatusTokens.Parse(sheet.Status);
        if (status is not (PesoStatus.Aprovado or PesoStatus.NaoAprovado))
        {
            throw new InvalidOperationException(
                $"Peso '{sheet.PesoId}' is not decided; a Peso PDF is only composed for decided " +
                "records (invariant: the service refuses before composing).");
        }

        // Data: the Job On PRODUCTION date — the control date of the record — resolved through the
        // shared read model's traversal facts (cm_id → jobon_id; never a second traversal inside
        // the document). SubmittedAt is submission/audit information only and is never shown as
        // the production Data; an absent production date is the truthful "—", never a substitute.
        var date = production.ProductionDate?.ToString("yyyy-MM-dd") ?? "—";

        return new PesoPdfDocumentModel(
            sheet.PesoId,
            sheet.Version,
            PesoStatusTokens.DisplayLabel(status.Value),
            production.Reference,
            production.ProductionNumber,
            production.Machine,
            date,
            context.FrozenToolReference,
            ToolTokens.ToToken(context.Tool.Processo),
            // The frozen CM lot, verbatim: a plain value, never prefixed with "L".
            context.FrozenToolLot,
            sheet.WaterTemperature,
            sheet.VolumeMarisaBq,
            sheet.VolumePuncaoPu,
            sheet.GlassDensityGCm3,
            sheet.PreviousProductionEndReference,
            sheet.PreviousAverageWeightReference,
            sheet.Rows
                .OrderBy(row => row.RowPosition)
                .Select(row => new PesoPdfRowModel(
                    row.RowPosition,
                    row.WaterWeightG,
                    row.CapacityCm3,
                    row.GlassWeightG))
                .ToList());
    }
}