using DMO.Application.ControloCreate;
using DMO.Application.JobOn;
using DMO.Domain.Controlo;
using DMO.Domain.Tools;

namespace DMO.UnitTests.Documents;

/// <summary>
/// Shared fixtures of the Peso PDF unit tests: decided/production-bound and pending sheet shapes of
/// the SHARED P2-T05 read model (the same read Create and Approve render).
/// </summary>
internal static class PesoPdfFixtures
{
    public static readonly DateTimeOffset CreatedAt = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset SubmittedAt = new(2026, 9, 21, 10, 30, 0, TimeSpan.Zero);

    /// <summary>A decided, production-bound sheet with <paramref name="rowCount"/> frozen rows.</summary>
    public static PesoSheetReadModel DecidedSheet(
        int rowCount = 4,
        PesoStatus status = PesoStatus.Aprovado,
        string machine = "B1") =>
        new(
            PesoId: new Guid("11111111-1111-1111-1111-111111111111"),
            Version: 2,
            Status: PesoStatusTokens.ToToken(status),
            CmId: new Guid("22222222-2222-2222-2222-222222222222"),
            ToolId: null,
            Context: new PesoContextProjection(
                CmId: new Guid("22222222-2222-2222-2222-222222222222"),
                ToolId: new Guid("33333333-3333-3333-3333-333333333333"),
                FrozenToolType: "CM",
                FrozenToolReference: "CM-1100",
                FrozenToolLot: "07",
                Tool: new ToolSummaryProjection(
                    ToolId: new Guid("33333333-3333-3333-3333-333333333333"),
                    Type: ToolType.Cm,
                    Reference: "CM-1100",
                    Lot: "07",
                    Processo: Processo.Nnpb,
                    Quantity: null,
                    CompatibleMachines: [])),
            Pending: null,
            Production: new PesoProductionProjection(
                Reference: "REF-X",
                ProductionNumber: "2026-001",
                Machine: machine,
                ProductionDate: new DateOnly(2026, 9, 19)),
            CreatedByUserId: new Guid("44444444-4444-4444-4444-444444444444"),
            CreatedAt,
            SubmittedByUserId: new Guid("55555555-5555-5555-5555-555555555555"),
            SubmittedAt,
            WaterTemperature: 25.0m,
            VolumeMarisaBq: null,
            VolumePuncaoPu: null,
            GlassDensityGCm3: 2.4027m,
            PreviousProductionEndReference: null,
            PreviousAverageWeightReference: null,
            Rows: Enumerable.Range(1, rowCount)
                .Select(index => new PesoRowReadModel(
                    new Guid($"{index:D2}000000-0000-0000-0000-000000000000"),
                    index,
                    WaterWeightG: 997.0m + index * 0.5m,
                    CapacityCm3: 1000.0m + index * 0.5m,
                    GlassWeightG: 2402.7m + index * 1.2m))
                .ToList());

    /// <summary>A submitted-but-undecided sheet (the reviewable predicate; generation refused).</summary>
    public static PesoSheetReadModel PendingStatusSheet() => DecidedSheet() with
    {
        Status = PesoStatusTokens.ToToken(PesoStatus.Pendente),
    };

    /// <summary>A decided sheet without production binding (Job On por associar; no target).</summary>
    public static PesoSheetReadModel PendingAnchorSheet() => DecidedSheet() with
    {
        CmId = null,
        ToolId = new Guid("33333333-3333-3333-3333-333333333333"),
        Context = null,
        Production = null,
        Pending = new PesoPendingProjection(
            ToolId: new Guid("33333333-3333-3333-3333-333333333333"),
            Tool: new ToolSummaryProjection(
                ToolId: new Guid("33333333-3333-3333-3333-333333333333"),
                Type: ToolType.Cm,
                Reference: "CM-1100",
                Lot: "07",
                Processo: Processo.Nnpb,
                Quantity: null,
                CompatibleMachines: [])),
    };
}