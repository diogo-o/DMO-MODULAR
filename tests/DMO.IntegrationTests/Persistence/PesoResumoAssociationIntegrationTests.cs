using DMO.Application.Controlo.Pesos;
using DMO.Application.JobOn;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Tools;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Controlo;
using DMO.Infrastructure.Persistence.ToolJobOn;
using DMO.IntegrationTests.Controlo.Pesos;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using DomainJobOn = DMO.Domain.JobOn.JobOn;
using DomainJobOnId = DMO.Domain.JobOn.JobOnId;
using DomainTool = DMO.Domain.Tools.Tool;
using DomainToolId = DMO.Domain.Tools.ToolId;

// Test-only raw SQL on the DISPOSABLE database: every interpolated value is a fixed, test-owned
// token (row identifiers derived from a fresh Guid). Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// Owner-clarification delta (P2-T05 §31) — env-gated integration tests over the disposable
/// PostgreSQL database: the Resumo da produção entry read (jobon_id → production context → cm_id)
/// and the pré-JobOn Peso association contract (peso.tool_id == cm_contexts.tool_id by the same
/// canonical UUID; same peso_id preserved; the direct Tool anchor ceases to be the active relation;
/// transactional and safe against repeated/concurrent association).
/// </summary>
/// <remarks>
/// The real services run against the real repositories over the disposable database (migrations
/// applied; the glass-density seed of migration 005 is the calculation configuration). Every row
/// this class creates carries a unique per-test token and is removed in <c>finally</c> in
/// FK-safe order.
/// </remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class PesoResumoAssociationIntegrationTests
{
    /// <summary>
    /// RES1 — the Resumo da produção of a Job On with a CM context receives the complete entry
    /// context from the real rows: reference, production number, machine, date, version, the real
    /// cm_id/tool_id, the frozen triple and the live Tool facts (reference, lot, processo, quantity).
    /// The frozen triple survives a later live-Tool change; the live facts reflect it (the two are
    /// separate facts of the Resumo, exactly like the accepted Peso context projection).
    /// </summary>
    [SkippableFact]
    public async Task RES1_ResumoDaProducaoRecebeOContextoDoJobOn()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";
        var toolReference = $"cm-{token}";

        var toolId = Guid.NewGuid();
        var jobOnId = DomainJobOnId.New();
        var productionDate = new DateOnly(2026, 3, 10);

        try
        {
            await CreateToolAsync(context, toolId, toolReference, "LOTE-1", "NNPB", 7);
            var jobOn = await CreateJobOnWithCmAsync(
                context, jobOnId.Value, reference, "P-1", "B1", productionDate, toolId, toolReference, "LOTE-1");
            var cmId = jobOn.Contexts.Single(contextEntry => contextEntry.ContextType == ToolContextType.Cm).ContextId;

            var resumo = new DmoProductionResumoRead(context);
            var value = await resumo.GetResumoAsync(jobOnId.Value, CancellationToken.None);

            Assert.NotNull(value);
            Assert.Equal(jobOnId.Value, value.JobOnId);
            Assert.Equal(reference, value.Reference);
            Assert.Equal("P-1", value.ProductionNumber);
            Assert.Equal("B1", value.Machine);
            Assert.Equal(productionDate, value.ProductionDate);
            Assert.Equal(1, value.Version);

            Assert.NotNull(value.Cm);
            Assert.Equal(cmId, value.Cm.CmId);
            Assert.Equal(toolId, value.Cm.ToolId);
            Assert.Equal("CM", value.Cm.FrozenToolType);
            Assert.Equal(toolReference, value.Cm.FrozenToolReference);
            Assert.Equal("LOTE-1", value.Cm.FrozenToolLot);
            Assert.Equal(toolReference, value.Cm.LiveToolReference);
            Assert.Equal("LOTE-1", value.Cm.LiveToolLot);
            Assert.Equal(Processo.Nnpb, value.Cm.Processo);
            Assert.Equal(7, value.Cm.Quantity);

            // A later live Tool change never rewrites the frozen triple; the Resumo carries both
            // facts separately (frozen ≠ live, exactly the P2-T04 §7.2 invariant).
            await UpdateToolReferenceAsync(context, toolId, $"{toolReference}-NOVO");

            var after = await resumo.GetResumoAsync(jobOnId.Value, CancellationToken.None);

            Assert.NotNull(after?.Cm);
            Assert.Equal(toolReference, after.Cm.FrozenToolReference);
            Assert.Equal($"{toolReference}-NOVO", after.Cm.LiveToolReference);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// RES2 — a Job On WITHOUT a CM context is still a valid Resumo: the production facts arrive and
    /// the CM projection is the truthful empty (missing-context recovery stays the operator's
    /// explicit path — never an invented context).
    /// </summary>
    [SkippableFact]
    public async Task RES2_ResumoDeProducaoSemContextoCmTrazOsFactosSemCm()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var jobOnId = DomainJobOnId.New();
            await CreateJobOnAsync(context, jobOnId.Value, reference, "P-2", "C3");

            var value = await new DmoProductionResumoRead(context)
                .GetResumoAsync(jobOnId.Value, CancellationToken.None);

            Assert.NotNull(value);
            Assert.Equal(reference, value.Reference);
            Assert.Equal("P-2", value.ProductionNumber);
            Assert.Equal("C3", value.Machine);
            Assert.Null(value.Cm);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// RES3 — an unknown jobon_id yields the empty (the HTTP surface maps it to 404 not-found,
    /// never an invented summary).
    /// </summary>
    [SkippableFact]
    public async Task RES3_ResumoDeJobOnDesconhecidoDevolveVazio()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var value = await new DmoProductionResumoRead(context)
            .GetResumoAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(value);
    }

    /// <summary>
    /// PRC1 — a Peso opened THROUGH the Resumo context is populated by the backend from the real
    /// identity chain: the create command carries ONLY the cm_id (plus the Peso-workflow facts) and
    /// the published sheet resolves máquina/referência/lote/processo and the CM context through
    /// cm_id → jobon_id + tool_id — no manual re-entry of production context.
    /// </summary>
    [SkippableFact]
    public async Task PRC1_PesoAbertoPeloResumoRecebeMachineReferenciaLoteProcessoECmSemReentrada()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";
        var toolReference = $"cm-{token}";
        var userId = Guid.NewGuid();

        var toolId = Guid.NewGuid();
        var jobOnId = DomainJobOnId.New();

        try
        {
            await CreateUserAsync(context, userId, token);
            await CreateToolAsync(context, toolId, toolReference, "LOTE-1", "NNPB", 7);
            var jobOn = await CreateJobOnWithCmAsync(
                context, jobOnId.Value, reference, "P-3", "B1", new DateOnly(2026, 3, 11), toolId, toolReference, "LOTE-1");
            var cmId = jobOn.Contexts.Single(entry => entry.ContextType == ToolContextType.Cm).ContextId;

            // Entry: jobon_id → Resumo → cm_id.
            var resumo = await new DmoProductionResumoRead(context)
                .GetResumoAsync(jobOnId.Value, CancellationToken.None);

            Assert.NotNull(resumo?.Cm);
            Assert.Equal(cmId, resumo.Cm.CmId);

            var created = await CreateControloService(context).CreateAsync(
                new CreatePesoCommand(
                    CmId: cmId,
                    PendingToolId: null,
                    WaterTemperature: 20m,
                    VolumeMarisaBq: null,
                    VolumePuncaoPu: null,
                    PreviousProductionEndReference: null,
                    PreviousAverageWeightReference: null,
                    RowWaterWeightsG: [998.2m, 997.1m],
                    CreatedByUserId: userId),
                CancellationToken.None);

            var pesoId = Assert.IsType<PesoResult.Created>(created).PesoId;

            // No re-entry evidence: the published sheet resolves every production fact through the
            // anchor — machine, reference, lote, processo and the CM identity/context.
            var found = await CreateControloService(context).GetAsync(pesoId, CancellationToken.None);
            var sheet = Assert.IsType<PesoResult.Found>(found).Sheet;

            Assert.Equal(cmId, sheet.CmId);
            Assert.Null(sheet.ToolId);

            Assert.NotNull(sheet.Production);
            Assert.Equal(reference, sheet.Production.Reference);
            Assert.Equal("P-3", sheet.Production.ProductionNumber);
            Assert.Equal("B1", sheet.Production.Machine);

            Assert.NotNull(sheet.Context);
            Assert.Equal(toolId, sheet.Context.ToolId);
            Assert.Equal(toolReference, sheet.Context.FrozenToolReference);
            Assert.Equal("LOTE-1", sheet.Context.FrozenToolLot);
            Assert.Equal(toolReference, sheet.Context.Tool.Reference);
            Assert.Equal("LOTE-1", sheet.Context.Tool.Lot);
            Assert.Equal(Processo.Nnpb, sheet.Context.Tool.Processo);
            Assert.Equal(2, sheet.Rows.Count);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// ASC1 — a peso_id created pré-JobOn (peso_id → tool_id, cm_id NULL) finds candidates ONLY
    /// among real cm_contexts rows whose cm_contexts.tool_id equals the SAME canonical tool UUID:
    /// a UUID belonging to another Tool is never a candidate and never appears (identity is the
    /// UUID, never reference/lote/máquina/texto).
    /// </summary>
    [SkippableFact]
    public async Task ASC1_PesoPreJobOnEncontraCmApenasQuandoOCmUsaOMesmoToolUuid()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        var toolX = Guid.NewGuid();
        var toolY = Guid.NewGuid();
        var jobOnX = DomainJobOnId.New();
        var jobOnY = DomainJobOnId.New();

        try
        {
            await CreateToolAsync(context, toolX, $"cm-x-{token}", "LX", "PS", null);
            await CreateToolAsync(context, toolY, $"cm-y-{token}", "LY", "NNPB", null);

            var occurrenceX = await CreateJobOnWithCmAsync(
                context, jobOnX.Value, $"{reference}-x", "PX", "B1", null, toolX, $"cm-x-{token}", "LX");
            var occurrenceY = await CreateJobOnWithCmAsync(
                context, jobOnY.Value, $"{reference}-y", "PY", "B2", null, toolY, $"cm-y-{token}", "LY");

            var cmX = occurrenceX.Contexts.Single(entry => entry.ContextType == ToolContextType.Cm).ContextId;
            var cmY = occurrenceY.Contexts.Single(entry => entry.ContextType == ToolContextType.Cm).ContextId;

            var service = CreateControloService(context);
            var candidateResult = await ((IControloCreateService)service)
                .ListAssociationCandidatesAsync(toolX, CancellationToken.None);

            var value = Assert.IsType<PesoResult.Candidates>(candidateResult).Value;

            var only = Assert.Single(value);
            Assert.Equal(cmX, only.CmContextId);
            Assert.Equal(jobOnX.Value, only.JobOnId);
            Assert.DoesNotContain(value, candidate => candidate.CmContextId == cmY);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// ASC2 — associating a pending peso with a cm_id whose context tool_id is a DIFFERENT UUID is
    /// refused (association-mismatch): nothing is written, the pending anchor stays the active
    /// relation and the version never bumps.
    /// </summary>
    [SkippableFact]
    public async Task ASC2_UuidDiferenteNaoAssociaENadaEscrito()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";
        var userId = Guid.NewGuid();

        var anchorTool = Guid.NewGuid();
        var otherTool = Guid.NewGuid();
        var otherJobOn = DomainJobOnId.New();

        try
        {
            await CreateUserAsync(context, userId, token);
            await CreateToolAsync(context, anchorTool, $"cm-a-{token}", "LA", "NNPB", null);
            await CreateToolAsync(context, otherTool, $"cm-o-{token}", "LO", "PS", null);

            var occurrence = await CreateJobOnWithCmAsync(
                context, otherJobOn.Value, $"{reference}-o", "PO", "C1", null, otherTool, $"cm-o-{token}", "LO");
            var otherCmId = occurrence.Contexts.Single(entry => entry.ContextType == ToolContextType.Cm).ContextId;

            var service = CreateControloService(context);
            var created = await service.CreateAsync(
                new CreatePesoCommand(
                    CmId: null,
                    PendingToolId: anchorTool,
                    WaterTemperature: 20m,
                    VolumeMarisaBq: null,
                    VolumePuncaoPu: null,
                    PreviousProductionEndReference: null,
                    PreviousAverageWeightReference: null,
                    RowWaterWeightsG: [998.2m],
                    CreatedByUserId: userId),
                CancellationToken.None);

            var pesoId = Assert.IsType<PesoResult.Created>(created).PesoId;

            var refused = await service.AssociateAsync(
                new AssociatePesoCommand(pesoId, otherCmId, ExpectedVersion: 1),
                CancellationToken.None);

            var refusal = Assert.IsType<PesoResult.Refused>(refused);
            Assert.Equal(PesoRefusalReason.AssociationMismatch, refusal.Reason);

            // Nothing written: same peso_id, still pending on the SAME tool UUID, version 1, rows intact.
            var found = await service.GetAsync(pesoId, CancellationToken.None);
            var sheet = Assert.IsType<PesoResult.Found>(found).Sheet;

            Assert.Null(sheet.CmId);
            Assert.Equal(anchorTool, sheet.ToolId);
            Assert.Equal(1, sheet.Version);
            Assert.Null(sheet.Production);
            Assert.Single(sheet.Rows);
            Assert.Equal(1, await CountPesosAsync(context, pesoId));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// ASC3 — associating with the candidate cm_id (same tool UUID) preserves the SAME peso_id:
    /// cm_id is set, the provisional direct tool_id anchor is cleared (it ceases to be the active
    /// operational relation), the version increments exactly once, one transaction, no copy.
    /// </summary>
    [SkippableFact]
    public async Task ASC3_AssociacaoPreservaOMesmoPesoIdEAncoraPassaACm()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";
        var userId = Guid.NewGuid();

        var toolId = Guid.NewGuid();
        var jobOnId = DomainJobOnId.New();

        try
        {
            await CreateUserAsync(context, userId, token);
            await CreateToolAsync(context, toolId, $"cm-{token}", "LOTE-1", "NNPB", 7);
            var jobOn = await CreateJobOnWithCmAsync(
                context, jobOnId.Value, reference, "P-4", "B1", null, toolId, $"cm-{token}", "LOTE-1");
            var cmId = jobOn.Contexts.Single(entry => entry.ContextType == ToolContextType.Cm).ContextId;

            var service = CreateControloService(context);
            var created = await service.CreateAsync(
                new CreatePesoCommand(
                    CmId: null,
                    PendingToolId: toolId,
                    WaterTemperature: 20m,
                    VolumeMarisaBq: null,
                    VolumePuncaoPu: null,
                    PreviousProductionEndReference: null,
                    PreviousAverageWeightReference: null,
                    RowWaterWeightsG: [998.2m, 997.1m],
                    CreatedByUserId: userId),
                CancellationToken.None);

            var pesoId = Assert.IsType<PesoResult.Created>(created).PesoId;

            var associated = await service.AssociateAsync(
                new AssociatePesoCommand(pesoId, cmId, ExpectedVersion: 1),
                CancellationToken.None);

            var done = Assert.IsType<PesoResult.Associated>(associated);
            Assert.Equal(pesoId, done.PesoId);
            Assert.Equal(2, done.Version);
            Assert.Equal(cmId, done.CmId);

            // Same single row: one peso_id, anchor swapped (cm set, direct tool cleared), rows intact.
            Assert.Equal(1, await CountPesosAsync(context, pesoId));

            var found = await service.GetAsync(pesoId, CancellationToken.None);
            var sheet = Assert.IsType<PesoResult.Found>(found).Sheet;

            Assert.Equal(pesoId, sheet.PesoId);
            Assert.Equal(cmId, sheet.CmId);
            Assert.Null(sheet.ToolId);
            Assert.Equal(2, sheet.Version);

            Assert.NotNull(sheet.Production);
            Assert.Equal(reference, sheet.Production.Reference);
            Assert.Equal("P-4", sheet.Production.ProductionNumber);
            Assert.Equal("B1", sheet.Production.Machine);

            Assert.Equal(2, sheet.Rows.Count);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// ASC4 — repeated association neither creates a duplicate nor silently changes the context:
    /// a second associate of the same peso (fresh observed version) is refused with
    /// already-associated even when it names a DIFFERENT cm_id, a stale-version repeat is refused
    /// with stale-version, exactly one pesos row remains and the anchor never moves.
    /// </summary>
    [SkippableFact]
    public async Task ASC4_AssociacaoRepetidaNaoCriaDuplicadoNemMudaDeContexto()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";
        var userId = Guid.NewGuid();

        var toolId = Guid.NewGuid();
        var otherTool = Guid.NewGuid();
        var jobOnId = DomainJobOnId.New();
        var otherJobOn = DomainJobOnId.New();

        try
        {
            await CreateUserAsync(context, userId, token);
            await CreateToolAsync(context, toolId, $"cm-{token}", "LOTE-1", "NNPB", null);
            await CreateToolAsync(context, otherTool, $"cm-o-{token}", "LOTE-O", "PS", null);

            var jobOn = await CreateJobOnWithCmAsync(
                context, jobOnId.Value, reference, "P-5", "B1", null, toolId, $"cm-{token}", "LOTE-1");
            var other = await CreateJobOnWithCmAsync(
                context, otherJobOn.Value, $"{reference}-o", "P-O", "B2", null, otherTool, $"cm-o-{token}", "LOTE-O");

            var cmId = jobOn.Contexts.Single(entry => entry.ContextType == ToolContextType.Cm).ContextId;
            var otherCmId = other.Contexts.Single(entry => entry.ContextType == ToolContextType.Cm).ContextId;

            var service = CreateControloService(context);
            var created = await service.CreateAsync(
                new CreatePesoCommand(
                    CmId: null,
                    PendingToolId: toolId,
                    WaterTemperature: 20m,
                    VolumeMarisaBq: null,
                    VolumePuncaoPu: null,
                    PreviousProductionEndReference: null,
                    PreviousAverageWeightReference: null,
                    RowWaterWeightsG: [998.2m],
                    CreatedByUserId: userId),
                CancellationToken.None);

            var pesoId = Assert.IsType<PesoResult.Created>(created).PesoId;

            var first = await service.AssociateAsync(
                new AssociatePesoCommand(pesoId, cmId, ExpectedVersion: 1),
                CancellationToken.None);
            Assert.IsType<PesoResult.Associated>(first);

            // Same cm, fresh version: the peso is already production-bound — refused, no duplicate.
            var repeat = await service.AssociateAsync(
                new AssociatePesoCommand(pesoId, cmId, ExpectedVersion: 2),
                CancellationToken.None);
            Assert.Equal(
                PesoRefusalReason.AlreadyAssociated,
                Assert.IsType<PesoResult.Refused>(repeat).Reason);

            // Different cm, fresh version: refused too — the anchor never silently moves.
            var move = await service.AssociateAsync(
                new AssociatePesoCommand(pesoId, otherCmId, ExpectedVersion: 2),
                CancellationToken.None);
            Assert.Equal(
                PesoRefusalReason.AlreadyAssociated,
                Assert.IsType<PesoResult.Refused>(move).Reason);

            // Stale-version replay of the first association: refused by the version token.
            var stale = await service.AssociateAsync(
                new AssociatePesoCommand(pesoId, cmId, ExpectedVersion: 1),
                CancellationToken.None);
            Assert.Equal(
                PesoRefusalReason.StaleVersion,
                Assert.IsType<PesoResult.Refused>(stale).Reason);

            // Exactly one row; the operational anchor is unchanged and the tool anchor stays cleared.
            Assert.Equal(1, await CountPesosAsync(context, pesoId));

            var found = await service.GetAsync(pesoId, CancellationToken.None);
            var sheet = Assert.IsType<PesoResult.Found>(found).Sheet;

            Assert.Equal(cmId, sheet.CmId);
            Assert.Null(sheet.ToolId);
            Assert.Equal(2, sheet.Version);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// ASC5 — concurrent double association is safe: two racing associates of the same pending
    /// peso (same observed version) settle with exactly one success and one typed refusal; never
    /// two rows, never a silent context change (version compare + concurrency token inside one
    /// transaction — the accepted SaveAsync pattern of §19.1).
    /// </summary>
    [SkippableFact]
    public async Task ASC5_AssociacaoConcorrenteNuncaDuplicaNemMudaSilenciosamente()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";
        var userId = Guid.NewGuid();

        var toolId = Guid.NewGuid();
        var jobOnId = DomainJobOnId.New();

        try
        {
            await CreateUserAsync(context, userId, token);
            await CreateToolAsync(context, toolId, $"cm-{token}", "LOTE-1", "NNPB", null);
            var jobOn = await CreateJobOnWithCmAsync(
                context, jobOnId.Value, reference, "P-6", "B1", null, toolId, $"cm-{token}", "LOTE-1");
            var cmId = jobOn.Contexts.Single(entry => entry.ContextType == ToolContextType.Cm).ContextId;

            var pesoId = Assert.IsType<PesoResult.Created>(
                    await CreateControloService(context).CreateAsync(
                        new CreatePesoCommand(
                            CmId: null,
                            PendingToolId: toolId,
                            WaterTemperature: 20m,
                            VolumeMarisaBq: null,
                            VolumePuncaoPu: null,
                            PreviousProductionEndReference: null,
                            PreviousAverageWeightReference: null,
                            RowWaterWeightsG: [998.2m],
                            CreatedByUserId: userId),
                        CancellationToken.None))
                .PesoId;

            // Two independent service instances over two independent contexts race the same
            // associate with the same observed version.
            await using var competitorContext = PersistenceTestDatabase.CreateContext();
            var competitorA = CreateControloService(context);
            var competitorB = CreateControloService(competitorContext);

            var results = await Task.WhenAll(
                competitorA.AssociateAsync(
                    new AssociatePesoCommand(pesoId, cmId, ExpectedVersion: 1),
                    CancellationToken.None),
                competitorB.AssociateAsync(
                    new AssociatePesoCommand(pesoId, cmId, ExpectedVersion: 1),
                    CancellationToken.None));

            var successes = results.OfType<PesoResult.Associated>().ToList();
            var refusals = results.OfType<PesoResult.Refused>().ToList();

            Assert.Single(successes);
            Assert.Single(refusals);
            Assert.Equal(PesoRefusalReason.StaleVersion, refusals[0].Reason);

            // Exactly one row, anchored on the SAME cm, tool anchor cleared.
            Assert.Equal(1, await CountPesosAsync(context, pesoId));

            var found = await CreateControloService(context).GetAsync(pesoId, CancellationToken.None);
            var sheet = Assert.IsType<PesoResult.Found>(found).Sheet;

            Assert.Equal(cmId, sheet.CmId);
            Assert.Null(sheet.ToolId);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // -------------------------------------------------------------------------------
    // Arrangement over the disposable database (real repositories; test-owned tokens)
    // -------------------------------------------------------------------------------

    private static ControloCreateService CreateControloService(DmoDbContext context)
    {
        var tools = new ToolRepository(context);
        var jobOns = new JobOnRepository(context);
        var pesos = new PesoRepository(context);

        var jobOnService = new JobOnService(jobOns, tools, []);
        var toolService = new ToolService(tools);

        return new ControloCreateService(
            pesos,
            new DmoPesoContextRead(context),
            jobOnService,
            toolService,
            new FixedCalculationConfiguration(),
            new GlassDensitySettingsRepository(context));
    }

    private static async Task CreateUserAsync(DmoDbContext context, Guid userId, string token)
    {
        var now = DateTimeOffset.UtcNow;

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO users
                (user_id, auth_identity_id, name, company_number, email, role, active, version, created_at, updated_at)
            VALUES
                ({0}, {1}, {2}, {3}, {4}, {5}, TRUE, 1, {6}, {6})
            """,
            userId,
            $"auth-{token}",
            $"Operador {token}",
            $"CN-{token}",
            $"operator-{token}@dmo.test",
            "Operador",
            now);
    }

    private static async Task CreateToolAsync(
        DmoDbContext context,
        Guid toolId,
        string reference,
        string lot,
        string processo,
        int? quantity)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO tools
                (tool_id, tool_type, reference, lot, processo, quantity, created_at, updated_at)
            VALUES
                ({0}, 'CM', {1}, {2}, {3}, {4}, now(), now())
            """,
            toolId,
            reference,
            lot,
            processo,
            new NpgsqlParameter("quantity", NpgsqlDbType.Integer)
            {
                Value = (object?)quantity ?? DBNull.Value,
            });
    }

    private static async Task<DomainJobOn> CreateJobOnAsync(
        DmoDbContext context,
        Guid jobOnId,
        string reference,
        string productionNumber,
        string machine,
        DateOnly? productionDate = null)
    {
        var jobOn = new DomainJobOn(
            DomainJobOnId.From(jobOnId),
            reference,
            productionNumber,
            MachineCode.From(machine),
            productionDate,
            CopiedFromJobOnId: null,
            Version: 1,
            Contexts: []);

        return await new JobOnRepository(context)
            .CreatedAsync(jobOn, [], CancellationToken.None);
    }

    private static async Task<DomainJobOn> CreateJobOnWithCmAsync(
        DmoDbContext context,
        Guid jobOnId,
        string reference,
        string productionNumber,
        string machine,
        DateOnly? productionDate,
        Guid toolId,
        string toolReference,
        string toolLot)
    {
        var jobOn = new DomainJobOn(
            DomainJobOnId.From(jobOnId),
            reference,
            productionNumber,
            MachineCode.From(machine),
            productionDate,
            CopiedFromJobOnId: null,
            Version: 1,
            Contexts: []);

        return await new JobOnRepository(context).CreatedAsync(
            jobOn,
            [
                new ToolContext(
                    ToolContextType.Cm,
                    Guid.NewGuid(),
                    DomainJobOnId.From(jobOnId),
                    DomainToolId.From(toolId),
                    new ToolContextSnapshot(ToolType.Cm, toolReference, toolLot)),
            ],
            CancellationToken.None);
    }

    private static Task UpdateToolReferenceAsync(DmoDbContext context, Guid toolId, string reference) =>
        context.Database.ExecuteSqlRawAsync(
            "UPDATE tools SET reference = {0}, updated_at = now() WHERE tool_id = {1}",
            reference,
            toolId);

    /// <summary>Counts the persistent <c>pesos</c> rows with the supplied id (exactly-one proof).</summary>
    private static async Task<int> CountPesosAsync(DmoDbContext context, Guid pesoId)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pesos WHERE peso_id = @p";
        command.Parameters.Add(new NpgsqlParameter("p", pesoId));
        await context.Database.OpenConnectionAsync();

        try
        {
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static Task CleanupAsync(DmoDbContext context, string token)
    {
        // The token appears inside every reference/company-number this test creates; the
        // CONTAINS match (LIKE '%token%') guarantees the rows of THIS test are the only ones
        // removed.
        return context.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM peso_measurement_rows
             WHERE peso_id IN (
                   SELECT peso_id FROM pesos
                    WHERE cm_id IN (
                          SELECT cm_id FROM cm_contexts
                           WHERE jobon_id IN (SELECT jobon_id FROM job_ons WHERE reference LIKE CONCAT('%', {0}, '%')))
                       OR tool_id IN (SELECT tool_id FROM tools WHERE reference LIKE CONCAT('%', {0}, '%')));
            DELETE FROM pesos
             WHERE cm_id IN (
                   SELECT cm_id FROM cm_contexts
                    WHERE jobon_id IN (SELECT jobon_id FROM job_ons WHERE reference LIKE CONCAT('%', {0}, '%')))
                OR tool_id IN (SELECT tool_id FROM tools WHERE reference LIKE CONCAT('%', {0}, '%'));
            DELETE FROM cm_contexts
             WHERE jobon_id IN (SELECT jobon_id FROM job_ons WHERE reference LIKE CONCAT('%', {0}, '%'));
            DELETE FROM job_ons WHERE reference LIKE CONCAT('%', {0}, '%');
            DELETE FROM tools WHERE reference LIKE CONCAT('%', {0}, '%');
            DELETE FROM users WHERE company_number LIKE CONCAT('%', {0}, '%');
            """,
            token);
    }
}