using DMO.Application.Documents;
using DMO.Domain.Controlo;

namespace DMO.UnitTests.Documents;

/// <summary>
/// Unit proofs of the pure Peso PDF composition: the document derives EVERY fact from the SHARED
/// P2-T05 read model (no parallel read, no recomputation — the per-row values pass through
/// frozen), the identification follows the fixed reading order, the lot is a plain value, no
/// Boquilha fact exists or is invented, and the per-CM averages come from THIS sheet only.
/// </summary>
public sealed class PesoPdfComposerTests
{
    [Fact]
    public void Compose_IdentifiesTheRecordWithTheFrozenFacts()
    {
        var sheet = PesoPdfFixtures.DecidedSheet();
        var document = PesoPdfComposer.Compose(sheet);

        Assert.Equal(sheet.PesoId, document.PesoId);
        Assert.Equal(sheet.Version, document.Version);
        Assert.Equal("Aprovado", document.EstadoLabel);

        // Target/traversal facts: verbatim from the sheet's production projection.
        Assert.Equal("REF-X", document.Reference);
        Assert.Equal("2026-001", document.ProductionNumber);
        Assert.Equal("B1", document.Machine);

        // Data: the Job On PRODUCTION date — the control date of the record (never SubmittedAt).
        Assert.Equal("2026-09-19", document.Date);

        // CM + Processo + Lote come from the frozen context projection; the lot is the plain
        // frozen value — never prefixed with "L" and never relabelled.
        Assert.Equal("CM-1100", document.CmReference);
        Assert.Equal("NNPB", document.Processo);
        Assert.Equal("07", document.Lot);
    }

    [Fact]
    public void Compose_PassesTheFrozenRowResultsThroughWithoutRecomputation()
    {
        var sheet = PesoPdfFixtures.DecidedSheet(rowCount: 6);
        var document = PesoPdfComposer.Compose(sheet);

        // N measurements — nothing is fixed at 4 rows and no row is dropped or re-derived.
        Assert.Equal(6, document.Rows.Count);
        foreach (var (source, rendered) in sheet.Rows.Zip(document.Rows))
        {
            Assert.Equal(source.RowPosition, rendered.Position);
            Assert.Equal(source.WaterWeightG, rendered.WaterWeightG);
            Assert.Equal(source.CapacityCm3, rendered.CapacityCm3);
            Assert.Equal(source.GlassWeightG, rendered.GlassWeightG);
        }

        Assert.Equal(sheet.GlassDensityGCm3, document.GlassDensityGCm3);
    }

    [Fact]
    public void Compose_ComputesThePerCmAveragesFromThisSheetOnly()
    {
        var sheet = PesoPdfFixtures.DecidedSheet(rowCount: 3);
        var document = PesoPdfComposer.Compose(sheet);

        var expectedAverageWater = sheet.Rows.Average(row => row.WaterWeightG);
        var expectedAverageCapacity = sheet.Rows.Average(row => row.CapacityCm3);
        var expectedAverageGlass = sheet.Rows.Average(row => row.GlassWeightG);

        // Per-CM values: the averages of THIS sheet's rows — never a global average.
        Assert.Equal(expectedAverageWater, document.AverageWaterWeightG);
        Assert.Equal(expectedAverageCapacity, document.AverageCapacityCm3);
        Assert.Equal(expectedAverageGlass, document.AverageGlassWeightG);
    }

    [Fact]
    public void Compose_RendersTheNotApprovedEstadoLabelVerbatim()
    {
        var document = PesoPdfComposer.Compose(
            PesoPdfFixtures.DecidedSheet(status: PesoStatus.NaoAprovado));

        Assert.Equal("Não aprovado", document.EstadoLabel);
    }

    [Fact]
    public void Compose_NeverSubstitutesSubmittedAtForTheProductionDate()
    {
        // A decided sheet WITHOUT a production date: Data is the truthful "—", never a fallback
        // to SubmittedAt/CreatedAt (SubmittedAt stays submission/audit information only).
        var sheet = PesoPdfFixtures.DecidedSheet() with
        {
            Production = PesoPdfFixtures.DecidedSheet().Production! with
            {
                ProductionDate = null,
            },
        };

        var document = PesoPdfComposer.Compose(sheet);

        Assert.Equal("—", document.Date);
        Assert.NotNull(sheet.SubmittedAt); // the audit fact exists — it is just not the Data
        Assert.NotEqual(sheet.SubmittedAt!.Value.ToString("yyyy-MM-dd"), document.Date);
    }

    [Fact]
    public void Compose_RefusesAnUndecidedSheet()
    {
        var sheet = PesoPdfFixtures.PendingStatusSheet();

        var exception = Assert.Throws<InvalidOperationException>(
            () => PesoPdfComposer.Compose(sheet));
        Assert.Contains("not decided", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_RefusesASheetWithoutProductionBinding()
    {
        var sheet = PesoPdfFixtures.PendingAnchorSheet();

        var exception = Assert.Throws<InvalidOperationException>(
            () => PesoPdfComposer.Compose(sheet));
        Assert.Contains("production binding", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}