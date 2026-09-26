using DMO.Domain.Controlo;
using DMO.Domain.Tools;
using DMO.IntegrationTests.Controlo.Pesos;
using PesoId = DMO.Domain.Controlo.PesoId;

namespace DMO.IntegrationTests.Controlo.Approve;

/// <summary>
/// P2-T06 test composition: the accepted P2-T05 composition (Peso/Definições store over the real
/// P2-T04 store) plus the in-memory review store, with direct arrangement helpers for the
/// reviewable fixtures.
/// </summary>
/// <remarks>
/// The Peso rows are seeded through the composition's OWN repository semantics (the same
/// <c>IPesoRepository</c> implementation the real ControloCreateService writes through), so the
/// HTTP rows prove the real P2-T06 orchestration over P2-T05 facts without a database. The
/// schema itself is proven by the env-gated DB-class tests.</remarks>
internal sealed class P2T06TestComposition
{
    /// <summary>The P2-T05 Peso/Definições composition (over the accepted P2-T04 store).</summary>
    public P2T05TestComposition Pesos { get; } = new();

    /// <summary>The in-memory review repository of the composition.</summary>
    public P2T06ReviewStore Review { get; }

    public P2T06TestComposition()
    {
        Review = new P2T06ReviewStore(Pesos);
    }

    // ---------------------------------------------------------------- arrangement helpers

    /// <summary>Seeds a canonical Tool and returns it (accepted P2-T04 store arrangement).</summary>
    public DMO.Domain.Tools.Tool SeedTool(
        ToolType type,
        string reference,
        string lot,
        Processo? processo = null,
        params string[] machines) =>
        Pesos.SeedTool(type, reference, lot, processo, quantity: null, machines);

    /// <summary>Seeds a production occurrence with its CM context (accepted arrangement).</summary>
    public DMO.Domain.JobOn.JobOn SeedJobOnWithCmContext(
        string reference,
        string productionNumber,
        string machine,
        Guid toolId,
        ToolType toolType,
        string toolReference,
        string toolLot) =>
        Pesos.SeedJobOnWithCmContext(reference, productionNumber, machine, toolId, toolType, toolReference, toolLot);

    /// <summary>
    /// Seeds a reviewable Peso (submitted, <c>pendente</c>) directly into the composition's Peso
    /// store through its own repository semantics (created → submitted on the SAME <c>peso_id</c>).
    /// Returns the stored <c>peso_id</c>.
    /// </summary>
    public async Task<Guid> SeedReviewablePesoAsync(
        Guid? cmId = null,
        Guid? toolId = null,
        DateTimeOffset? submittedAt = null,
        Guid? submittedBy = null,
        Guid? createdBy = null)
    {
        var now = DateTimeOffset.UtcNow;
        var pesoId = PesoId.New();
        var creator = createdBy ?? Guid.NewGuid();
        var submitter = submittedBy ?? P2T06TestHost.ActorUserId;

        var peso = new Peso(
            pesoId,
            cmId,
            toolId,
            PesoStatus.Pendente,
            SubmittedAt: null,
            SubmittedByUserId: null,
            WaterTemperature: 25m,
            VolumeMarisaBq: null,
            VolumePuncaoPu: null,
            GlassDensityGCm3: 2.4027m,
            PreviousProductionEndReference: null,
            PreviousAverageWeightReference: null,
            Version: 1,
            CreatedByUserId: creator,
            CreatedAt: now,
            UpdatedAt: now,
            Rows:
            [
                new PesoMeasurementRow(
                    PesoMeasurementRowId.New(),
                    pesoId,
                    1,
                    997.1m,
                    1000m,
                    2402.7000m,
                    now),
            ]);

        var created = await Pesos.CreatedAsync(peso, peso.Rows, CancellationToken.None);

        var submitted = created with
        {
            SubmittedAt = submittedAt ?? now.AddMinutes(-1),
            SubmittedByUserId = submitter,
        };

        await Pesos.SubmittedAsync(submitted, CancellationToken.None);

        return created.PesoId.Value;
    }
}