using DMO.Application.Accounts;
using DMO.Application.Boquilhas;
using DMO.Application.JobOn;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Application.Session;
using DMO.Domain.Boquilhas;
using DMO.Domain.Tools;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Boquilhas;
using DMO.Infrastructure.Persistence.Controlo;
using DMO.Infrastructure.Persistence.ToolJobOn;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using DomainJobOn = DMO.Domain.JobOn.JobOn;
using DomainJobOnId = DMO.Domain.JobOn.JobOnId;
using DomainToolId = DMO.Domain.Tools.ToolId;

// Test-only raw SQL on the DISPOSABLE database: every interpolated value is a fixed, test-owned
// token (row identifiers derived from a fresh Guid). Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// Owner-clarification delta (P2-T07 §34) — env-gated integration tests over the disposable
/// PostgreSQL database: the transitional pré-JobOn register (anchored on the canonical BQ
/// <c>tool_id</c>), the human-confirmed association at the matching <c>bq_id</c> (the SAME UUID
/// proof, the SAME <c>boquilhas_id</c>, history untouched, version bumped once, typed refusals),
/// the Job-On-incoming pending-registers read and the Boquilhas-owned repairer family (independent
/// B1…C3 assignments; historical repairer facts never rewritten).
/// </summary>
/// <remarks>
/// The real services run against the real repositories over the disposable database (all eight
/// migrations applied). Every row this class creates carries a unique per-test token and is
/// removed in <c>finally</c> in FK-safe order.
/// </remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class BoquilhasPreJobonAssociationIntegrationTests
{
    /// <summary>
    /// PB1 — a Boquilhas register can exist BEFORE any Job On, anchored ONLY on the real canonical
    /// BQ Tool (<c>boquilhas.tool_id</c>, <c>bq_id</c> NULL, version 1); legitimate work
    /// (movements) is recorded on the pending anchor; the anchor CHECK refuses both/neither
    /// anchors; the Tool FK protects the anchor (23503).
    /// </summary>
    [SkippableFact]
    public async Task PB1_PreJobOnRegisterExistsAnchoredOnTheCanonicalBqTool()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var toolReference = $"bq-{token}";
        var userId = Guid.NewGuid();
        var toolId = Guid.NewGuid();

        try
        {
            await CreateUserAsync(context, userId, token);
            await CreateToolAsync(context, toolId, toolReference, "LOTE-1", ToolType.Bq);
            var repairerId = await CreateRepairerAsync(context, token, "Reparador A");

            var repository = new BoquilhasRepository(context);
            var created = await repository.CreatedAsync(
                new BoqCreateUnit(BqId: null, PendingToolId: toolId, userId),
                CancellationToken.None);

            // The pending row: bq_id NULL, tool_id set, version 1, no quantity event.
            Assert.True(created.IsPending);
            Assert.Null(created.BqId);
            Assert.Equal(toolId, created.ToolId);
            Assert.Equal(1, created.Version);
            Assert.Empty(created.Movements);

            Assert.Equal(1, await QueryIntAsync(context, "SELECT count(*) FROM boquilhas"));
            var row = await QueryRowAsync(context, created.BoquilhasId.Value);
            Assert.True(row.BqIdNull);
            Assert.Equal(toolId, row.ToolId);

            // Legitimate pré-JobOn work: movements are recorded on the pending anchor.
            var appended = await repository.AppendMovementAsync(
                new BoqAppendUnit(
                    created.BoquilhasId.Value,
                    "saida",
                    8,
                    new DateOnly(2026, 9, 25),
                    "B1",
                    repairerId,
                    Observations: null,
                    RecordedByUserId: userId),
                CancellationToken.None);
            Assert.Equal(8, appended.Quantity);

            var after = await repository.GetByIdAsync(created.BoquilhasId.Value, CancellationToken.None);
            Assert.NotNull(after);
            Assert.Equal(8, after!.Outstanding);

            // The anchor CHECK: both anchors and neither anchor are impossible at the DB.
            var both = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
                context,
                "INSERT INTO boquilhas (boquilhas_id, bq_id, tool_id, version, created_by_user_id, created_at) " +
                "VALUES (@id, @bq, @tool, 1, @user, now())",
                ("id", (object)Guid.NewGuid()),
                ("bq", (object)Guid.NewGuid()),
                ("tool", (object)toolId),
                ("user", (object)userId)));
            Assert.Equal("23514", both.SqlState);
            Assert.Contains("boquilhas_anchor_check", both.Message, StringComparison.Ordinal);

            var neither = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
                context,
                "INSERT INTO boquilhas (boquilhas_id, bq_id, tool_id, version, created_by_user_id, created_at) " +
                "VALUES (@id, NULL, NULL, 1, @user, now())",
                ("id", (object)Guid.NewGuid()),
                ("user", (object)userId)));
            Assert.Equal("23514", neither.SqlState);

            // The Tool FK protects the pending anchor (23503 backstop).
            var deleteGuard = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
                context,
                "DELETE FROM tools WHERE tool_id = @tool",
                ("tool", (object)toolId)));
            Assert.Equal("23503", deleteGuard.SqlState);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// PB2 — a candidate <c>bq_id</c> of a DIFFERENT canonical Tool UUID is not offered (the
    /// candidates read is empty) and the association is refused typed
    /// (<c>association-mismatch</c>) — nothing is written; the reference/lote/máquina never
    /// substitute the UUID identity.
    /// </summary>
    [SkippableFact]
    public async Task PB2_DifferentUuidContextIsNotOfferedAndTheAssociationIsRefused()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var pendingToolId = Guid.NewGuid();
        var otherToolId = Guid.NewGuid();
        var otherJobOnId = DomainJobOnId.New();

        try
        {
            await CreateUserAsync(context, userId, token);
            await CreateToolAsync(context, pendingToolId, $"bq-pending-{token}", "LOTE-1", ToolType.Bq);
            await CreateToolAsync(context, otherToolId, $"bq-other-{token}", "LOTE-X", ToolType.Bq);
            var otherJobOn = await CreateJobOnWithBqAsync(
                context, otherJobOnId.Value, $"ref-other-{token}", "P-OTHER", "C3", null, otherToolId, $"bq-other-{token}", "LOTE-X");
            var otherBqId = otherJobOn.Contexts.Single(entry => entry.ContextType == ToolContextType.Bq).ContextId;

            var service = CreateService(context, userId);
            var created = Assert.IsType<BoquilhasResult.RegisterCreated>(
                await service.CreateAsync(
                    new CreateBoquilhaRegisterCommand(BqId: null, PendingToolId: pendingToolId, CreatedByUserId: userId),
                    CancellationToken.None));

            // The candidates read is EMPTY: the other tool's context is never offered.
            var candidates = Assert.IsType<BoquilhasResult.AssociationCandidatesFound>(
                await service.GetAssociationCandidatesAsync(created.BoquilhasId, CancellationToken.None));
            Assert.Empty(candidates.Candidates);

            // The explicit associate is refused typed: association-mismatch, nothing written.
            var refused = Assert.IsType<BoquilhasResult.Refused>(
                await service.AssociateAsync(
                    new AssociateBoquilhasCommand(created.BoquilhasId, otherBqId, ExpectedVersion: 1),
                    CancellationToken.None));
            Assert.Equal(BoquilhasRefusalReason.AssociationMismatch, refused.Reason);

            var read = await new BoquilhasRepository(context).GetByIdAsync(created.BoquilhasId, CancellationToken.None);
            Assert.NotNull(read);
            Assert.True(read!.IsPending);
            Assert.Equal(pendingToolId, read.ToolId);
            Assert.Equal(1, read.Version);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// PB3 — the SAME canonical UUID produces the candidate: the candidates read surfaces the REAL
    /// <c>bq_contexts</c> row of the same Tool with the REAL Job On facts; the confirmed
    /// association succeeds on the SAME <c>boquilhas_id</c>.
    /// </summary>
    [SkippableFact]
    public async Task PB3_SameUuidProducesTheCandidate_AndTheConfirmedAssociationSucceeds()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var toolId = Guid.NewGuid();
        var jobOnId = DomainJobOnId.New();
        var repairerId = Guid.NewGuid();

        try
        {
            await CreateUserAsync(context, userId, token);
            await CreateToolAsync(context, toolId, $"bq-{token}", "LOTE-1", ToolType.Bq);
            await CreateRepairerAsync(context, token, "Reparador A", repairerId);
            var jobOn = await CreateJobOnWithBqAsync(
                context, jobOnId.Value, $"ref-{token}", "P-1", "B1", new DateOnly(2026, 9, 27), toolId, $"bq-{token}", "LOTE-1");
            var bqId = jobOn.Contexts.Single(entry => entry.ContextType == ToolContextType.Bq).ContextId;

            var repository = new BoquilhasRepository(context);
            var service = CreateService(context, userId);

            // Pré-JobOn work: a pending register with real movements.
            var created = Assert.IsType<BoquilhasResult.RegisterCreated>(
                await service.CreateAsync(
                    new CreateBoquilhaRegisterCommand(BqId: null, PendingToolId: toolId, CreatedByUserId: userId),
                    CancellationToken.None));
            Assert.IsType<BoquilhasResult.MovementAppended>(
                await service.AppendMovementAsync(
                    new AppendMovementCommand(
                        created.BoquilhasId,
                        "saida",
                        10,
                        new DateOnly(2026, 9, 25),
                        "B1",
                        repairerId,
                        Observations: null),
                    CancellationToken.None));

            // The SAME UUID: the candidate is the real context of the same Job On.
            var candidates = Assert.IsType<BoquilhasResult.AssociationCandidatesFound>(
                await service.GetAssociationCandidatesAsync(created.BoquilhasId, CancellationToken.None));
            var candidate = Assert.Single(candidates.Candidates);
            Assert.Equal(bqId, candidate.BqId);
            Assert.Equal(jobOnId.Value, candidate.JobOnId);
            Assert.Equal($"ref-{token}", candidate.Reference);
            Assert.Equal("P-1", candidate.ProductionNumber);

            var associated = Assert.IsType<BoquilhasResult.Associated>(
                await service.AssociateAsync(
                    new AssociateBoquilhasCommand(created.BoquilhasId, bqId, ExpectedVersion: 1),
                    CancellationToken.None));

            Assert.Equal(created.BoquilhasId, associated.BoquilhasId);
            Assert.Equal(2, associated.Version);
            Assert.Equal(bqId, associated.BqId);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// PB4/PB5 — the association is ONLY the explicit action (the mere existence of the production
    /// never associates — nothing is silent or automatic), and the settled register PRESERVES the
    /// same <c>boquilhas_id</c> and the whole history: one row, movements untouched, bq_id set,
    /// provisional tool anchor cleared, version 2; the operational relation becomes
    /// <c>boquilhas_id → bq_id → jobon_id</c> and the ficha resolves the production facts.
    /// </summary>
    [SkippableFact]
    public async Task PB4PB5_AssociationIsExplicitAndPreservesTheSameRegisterAndItsHistory()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var toolId = Guid.NewGuid();
        var jobOnId = DomainJobOnId.New();
        var repairerId = Guid.NewGuid();

        try
        {
            await CreateUserAsync(context, userId, token);
            await CreateToolAsync(context, toolId, $"bq-{token}", "LOTE-1", ToolType.Bq);
            await CreateRepairerAsync(context, token, "Reparador A", repairerId);
            var jobOn = await CreateJobOnWithBqAsync(
                context, jobOnId.Value, $"ref-{token}", "P-1", "B1", null, toolId, $"bq-{token}", "LOTE-1");
            var bqId = jobOn.Contexts.Single(entry => entry.ContextType == ToolContextType.Bq).ContextId;

            var repository = new BoquilhasRepository(context);
            var service = CreateService(context, userId);

            var created = Assert.IsType<BoquilhasResult.RegisterCreated>(
                await service.CreateAsync(
                    new CreateBoquilhaRegisterCommand(BqId: null, PendingToolId: toolId, CreatedByUserId: userId),
                    CancellationToken.None));
            var pendingId = created.BoquilhasId;

            await service.AppendMovementAsync(
                new AppendMovementCommand(pendingId, "saida", 10, new DateOnly(2026, 9, 25), "B1", repairerId, null),
                CancellationToken.None);
            await service.AppendMovementAsync(
                new AppendMovementCommand(pendingId, "entrada", 3, new DateOnly(2026, 9, 26), null, null, null),
                CancellationToken.None);

            // Before the explicit act: still pending (no silent association).
            var before = await repository.GetByIdAsync(pendingId, CancellationToken.None);
            Assert.True(before!.IsPending);

            // The explicit human act.
            await service.AssociateAsync(
                new AssociateBoquilhasCommand(pendingId, bqId, ExpectedVersion: 1),
                CancellationToken.None);

            // Exactly ONE row — the SAME boquilhas_id; no duplicate, no new aggregate.
            Assert.Equal(1, await QueryIntAsync(
                context,
                "SELECT count(*) FROM boquilhas WHERE boquilhas_id = @id",
                ("id", (object)pendingId)));

            var after = await repository.GetByIdAsync(pendingId, CancellationToken.None);
            Assert.NotNull(after);
            Assert.False(after!.IsPending);
            Assert.Equal(bqId, after.BqId);
            Assert.Null(after.ToolId);
            Assert.Equal(2, after.Version);
            Assert.Equal(2, after.Movements.Count);
            Assert.Equal(7, after.Outstanding);
            Assert.Equal(repairerId, after.Movements.First(movement => movement.Kind == MovementKind.Saida).RepairerId);

            // The operational relation is boquilhas_id → bq_id → jobon_id.
            var chain = await QueryLinkAsync(context, pendingId);
            Assert.Equal(bqId, chain.BqId);
            Assert.Equal(jobOnId.Value, chain.JobOnId);

            // The ficha resolves the production facts through the real chain.
            var ficha = Assert.IsType<BoquilhasResult.Ficha>(
                await service.GetAsync(pendingId, CancellationToken.None));
            Assert.NotNull(ficha.Value.Production);
            Assert.Equal($"ref-{token}", ficha.Value.Production.Reference);
            Assert.Equal("P-1", ficha.Value.Production.ProductionNumber);
            Assert.Equal(2, ficha.Value.Movements.Count);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// PB6 — a repeated association is refused TYPED (<c>already-associated</c>) and never
    /// duplicates; a concurrent double association settles with EXACTLY one success and one typed
    /// refusal (stale-version), one row, version 2.
    /// </summary>
    [SkippableFact]
    public async Task PB6_RepeatedAndConcurrentAssociationsSettleWithoutDuplication()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var toolId = Guid.NewGuid();
        var jobOnId = DomainJobOnId.New();

        try
        {
            await CreateUserAsync(context, userId, token);
            await CreateToolAsync(context, toolId, $"bq-{token}", "LOTE-1", ToolType.Bq);
            var jobOn = await CreateJobOnWithBqAsync(
                context, jobOnId.Value, $"ref-{token}", "P-1", "B1", null, toolId, $"bq-{token}", "LOTE-1");
            var bqId = jobOn.Contexts.Single(entry => entry.ContextType == ToolContextType.Bq).ContextId;

            var repository = new BoquilhasRepository(context);
            var service = CreateService(context, userId);

            var created = Assert.IsType<BoquilhasResult.RegisterCreated>(
                await service.CreateAsync(
                    new CreateBoquilhaRegisterCommand(BqId: null, PendingToolId: toolId, CreatedByUserId: userId),
                    CancellationToken.None));
            var pendingId = created.BoquilhasId;

            // Two independent service instances over two independent contexts race the same
            // associate with the same observed version.
            await using var competitorContext = PersistenceTestDatabase.CreateContext();
            var competitorA = CreateService(context, userId);
            var competitorB = CreateService(competitorContext, userId);

            var results = await Task.WhenAll(
                competitorA.AssociateAsync(
                    new AssociateBoquilhasCommand(pendingId, bqId, ExpectedVersion: 1),
                    CancellationToken.None),
                competitorB.AssociateAsync(
                    new AssociateBoquilhasCommand(pendingId, bqId, ExpectedVersion: 1),
                    CancellationToken.None));

            var successes = results.OfType<BoquilhasResult.Associated>().ToList();
            var refusals = results.OfType<BoquilhasResult.Refused>().ToList();

            Assert.Single(successes);
            Assert.Single(refusals);
            Assert.Equal(BoquilhasRefusalReason.StaleVersion, refusals[0].Reason);

            // Exactly one row, anchored on the SAME bq, tool anchor cleared, version 2.
            Assert.Equal(1, await QueryIntAsync(
                context,
                "SELECT count(*) FROM boquilhas WHERE boquilhas_id = @id",
                ("id", (object)pendingId)));

            var after = await repository.GetByIdAsync(pendingId, CancellationToken.None);
            Assert.NotNull(after);
            Assert.False(after!.IsPending);
            Assert.Equal(bqId, after.BqId);
            Assert.Equal(2, after.Version);

            // A THIRD, repeated association is refused typed (already-associated), not duplicated.
            var again = Assert.IsType<BoquilhasResult.Refused>(
                await service.AssociateAsync(
                    new AssociateBoquilhasCommand(pendingId, bqId, ExpectedVersion: 2),
                    CancellationToken.None));
            Assert.Equal(BoquilhasRefusalReason.AlreadyAssociated, again.Reason);
            Assert.Equal(1, await QueryIntAsync(
                context,
                "SELECT count(*) FROM boquilhas WHERE boquilhas_id = @id",
                ("id", (object)pendingId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// PB7 — the Job-On-incoming read is a SPECIFIC, tool-keyed light packet: given the
    /// production's <c>bq_id</c>, only the pending registers of ITS canonical Tool are returned
    /// (another tool's pending register is never offered; the production's own register is never
    /// offered again).
    /// </summary>
    [SkippableFact]
    public async Task PB7_PendingRegistersAreKeyedByTheProductionCanonicalTool()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var toolId = Guid.NewGuid();
        var otherToolId = Guid.NewGuid();
        var jobOnId = DomainJobOnId.New();

        try
        {
            await CreateUserAsync(context, userId, token);
            await CreateToolAsync(context, toolId, $"bq-{token}", "LOTE-1", ToolType.Bq);
            await CreateToolAsync(context, otherToolId, $"bq-other-{token}", "LOTE-X", ToolType.Bq);
            var jobOn = await CreateJobOnWithBqAsync(
                context, jobOnId.Value, $"ref-{token}", "P-1", "B1", null, toolId, $"bq-{token}", "LOTE-1");
            var bqId = jobOn.Contexts.Single(entry => entry.ContextType == ToolContextType.Bq).ContextId;

            var service = CreateService(context, userId);

            // The same tool's pending register (with work) and another tool's pending register.
            var samePending = Assert.IsType<BoquilhasResult.RegisterCreated>(
                await service.CreateAsync(
                    new CreateBoquilhaRegisterCommand(BqId: null, PendingToolId: toolId, CreatedByUserId: userId),
                    CancellationToken.None));
            await service.CreateAsync(
                new CreateBoquilhaRegisterCommand(BqId: null, PendingToolId: otherToolId, CreatedByUserId: userId),
                CancellationToken.None);

            var registos = Assert.IsType<BoquilhasResult.PendingRegistersFound>(
                await service.GetPendingRegistersAsync(bqId, CancellationToken.None));

            var row = Assert.Single(registos.Registers);
            Assert.Equal(samePending.BoquilhasId, row.BoquilhasId);
            Assert.Equal(toolId, row.ToolId);
            Assert.Equal($"bq-{token}", row.ToolReference);
            Assert.Equal(1, row.Version);
            Assert.Equal(0, row.Outstanding);

            // After the confirmed association the production's own register is, of course, no
            // longer pending — the read is truthful.
            await service.AssociateAsync(
                new AssociateBoquilhasCommand(samePending.BoquilhasId, bqId, ExpectedVersion: 1),
                CancellationToken.None);

            var after = Assert.IsType<BoquilhasResult.PendingRegistersFound>(
                await service.GetPendingRegistersAsync(bqId, CancellationToken.None));
            Assert.Empty(after.Registers);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// PB8 — the B1/B2/B3/C1/C2/C3 assignments are INDEPENDENT (the Boquilhas Definições surface,
    /// §34.3): setting/changing ONE machine never touches the other five; a changed default
    /// repairer NEVER rewrites the historical movement's <c>repairer_id</c>.
    /// </summary>
    [SkippableFact]
    public async Task PB8_AssignmentsAreIndependent_AndHistoryKeepsTheUsedRepairer()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var toolId = Guid.NewGuid();
        var jobOnId = DomainJobOnId.New();
        var repairerA = Guid.NewGuid();
        var repairerB = Guid.NewGuid();

        try
        {
            await CreateUserAsync(context, userId, token);
            await CreateToolAsync(context, toolId, $"bq-{token}", "LOTE-1", ToolType.Bq);
            await CreateRepairerAsync(context, token, "Reparador A", repairerA);
            await CreateRepairerAsync(context, token, "Reparador B", repairerB);
            var jobOn = await CreateJobOnWithBqAsync(
                context, jobOnId.Value, $"ref-{token}", "P-1", "B1", null, toolId, $"bq-{token}", "LOTE-1");
            var bqId = jobOn.Contexts.Single(entry => entry.ContextType == ToolContextType.Bq).ContextId;

            var definitions = new BoquilhasDefinicoesService(
                new RepairerRepository(context),
                new MachineRepairerAssignmentRepository(context));

            // Every machine gets its OWN independent assignment.
            foreach (var machine in MachineCode.All)
            {
                var set = Assert.IsType<BoquilhasDefinicoesResult.AssignmentSet>(
                    await definitions.SetMachineAssignmentAsync(
                        new SetMachineAssignmentCommand(machine.Value, repairerA, ExpectedVersion: null),
                        CancellationToken.None));
                Assert.Equal(machine.Value, set.Machine);
                Assert.Equal(1, set.Version);
            }

            // Change ONLY B1's default (B1 → repairer B). The other five keep repairer A.
            await definitions.SetMachineAssignmentAsync(
                new SetMachineAssignmentCommand("B1", repairerB, ExpectedVersion: 1),
                CancellationToken.None);

            var assignments = Assert.IsType<BoquilhasDefinicoesResult.AssignmentsFound>(
                await definitions.ListMachineAssignmentsAsync(CancellationToken.None)).Assignments;
            var byMachine = assignments.ToDictionary(assignment => assignment.Machine, assignment => assignment);

            Assert.Equal(repairerB, byMachine["B1"].RepairerId);
            Assert.Equal(2, byMachine["B1"].Version);
            foreach (var machine in new[] { "B2", "B3", "C1", "C2", "C3" })
            {
                Assert.Equal(repairerA, byMachine[machine].RepairerId);
                Assert.Equal(1, byMachine[machine].Version);
            }

            // A HISTORICAL movement recorded with repairer A on B1 keeps repairer A after the
            // default change — the movement freezes the repairer used at the time.
            var repository = new BoquilhasRepository(context);
            var registered = await repository.CreatedAsync(
                new BoqCreateUnit(bqId, PendingToolId: null, userId),
                CancellationToken.None);
            var movement = await repository.AppendMovementAsync(
                new BoqAppendUnit(
                    registered.BoquilhasId.Value,
                    "saida",
                    5,
                    new DateOnly(2026, 9, 25),
                    "B1",
                    repairerA,
                    Observations: null,
                    RecordedByUserId: userId),
                CancellationToken.None);

            var historical = await repository.GetByIdAsync(registered.BoquilhasId.Value, CancellationToken.None);
            var stored = historical!.Movements.Single(candidate => candidate.MovementId == movement.MovementId);
            Assert.Equal(repairerA, stored.RepairerId);

            var raw = await QueryRepairerAsync(context, movement.MovementId.Value);
            Assert.Equal(repairerA, raw);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// PB9 — ONLY <c>saida | entrada | entrada_sem_reparacao</c> remain valid: the DB CHECK
    /// rejects any other token (23514) and the repository maps it to the typed
    /// <c>MOVEMENT_TYPE_INVALID</c> — no Início, no Irreparável, no legacy type.
    /// </summary>
    [SkippableFact]
    public async Task PB9_OnlyTheThreeClosedMovementTypesRemainValid()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var toolId = Guid.NewGuid();

        try
        {
            await CreateUserAsync(context, userId, token);
            await CreateToolAsync(context, toolId, $"bq-{token}", "LOTE-1", ToolType.Bq);

            var repository = new BoquilhasRepository(context);
            var created = await repository.CreatedAsync(
                new BoqCreateUnit(BqId: null, PendingToolId: toolId, userId),
                CancellationToken.None);

            // The repository maps the legacy token to the typed validator token.
            var refused = await Assert.ThrowsAsync<BoquilhasPersistenceException>(() =>
                repository.AppendMovementAsync(
                    new BoqAppendUnit(
                        created.BoquilhasId.Value,
                        "inicio",
                        1,
                        new DateOnly(2026, 9, 25),
                        null,
                        null,
                        Observations: null,
                        RecordedByUserId: userId),
                    CancellationToken.None));
            Assert.Equal(BoquilhasPersistenceFailureReason.ConstraintViolation, refused.Reason);
            Assert.Equal(BoquilhasValidationErrors.MovementTypeInvalid, refused.ValidatorToken);

            // The DB CHECK backstop rejects the raw row too.
            var raw = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
                context,
                "INSERT INTO boquilha_movements " +
                "(movement_id, boquilhas_id, movement_type, quantity, business_date, recorded_at, recorded_by_user_id, version, created_at, updated_at) " +
                "VALUES (@movementId, @registerId, 'inicio', 1, '2026-09-25', now(), @user, 1, now(), now())",
                ("movementId", (object)Guid.NewGuid()),
                ("registerId", (object)created.BoquilhasId.Value),
                ("user", (object)userId)));
            Assert.Equal("23514", raw.SqlState);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // -------------------------------------------------------------------------------
    // Arrangement over the disposable database (real repositories; test-owned tokens)
    // -------------------------------------------------------------------------------

    private static BoquilhasService CreateService(DmoDbContext context, Guid userId) =>
        new(
            new BoquilhasRepository(context),
            new JobOnService(new JobOnRepository(context), new ToolRepository(context), []),
            new ToolRepository(context),
            new RepairerRepository(context),
            new MachineRepairerAssignmentRepository(context),
            new DmoBoquilhasContextRead(context),
            new FixedCurrentAccount(userId));

    /// <summary>Fixed backend actor of every service write in this class.</summary>
    private sealed class FixedCurrentAccount : ICurrentAccountContext
    {
        private readonly Guid _accountId;

        public FixedCurrentAccount(Guid accountId)
        {
            _accountId = accountId;
        }

        public Task<CurrentAccount> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult<CurrentAccount>(new CurrentAccount.User(new UserAccount(
                _accountId,
                "CN-FIXED",
                "Operador de teste",
                "operator@dmo.test",
                "Operador",
                IsActive: true,
                TemplateId: null,
                Version: 1)));
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
        ToolType type)
    {
        var typeToken = type switch
        {
            ToolType.Cm => "CM",
            ToolType.Mf => "MF",
            _ => "BQ",
        };

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO tools
                (tool_id, tool_type, reference, lot, processo, quantity, created_at, updated_at)
            VALUES
                ({0}, {1}, {2}, {3}, NULL, NULL, now(), now())
            """,
            toolId,
            typeToken,
            reference,
            lot);
    }

    private static async Task<Guid> CreateRepairerAsync(
        DmoDbContext context,
        string token,
        string displayName,
        Guid? repairerId = null)
    {
        var id = repairerId ?? Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO repairers
                (repairer_id, name, version, created_at, updated_at)
            VALUES
                ({0}, {1}, 1, {2}, {2})
            """,
            id,
            $"{displayName} {token}",
            now);

        return id;
    }

    private static async Task<DomainJobOn> CreateJobOnWithBqAsync(
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
                    ToolContextType.Bq,
                    Guid.NewGuid(),
                    DomainJobOnId.From(jobOnId),
                    DomainToolId.From(toolId),
                    new ToolContextSnapshot(ToolType.Bq, toolReference, toolLot)),
            ],
            CancellationToken.None);
    }

    // ------------------------------------------------------------------ raw evidence reads

    private static async Task<int> QueryIntAsync(DmoDbContext context, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter(name, value));
        }

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

    private static async Task<(bool BqIdNull, Guid ToolId)> QueryRowAsync(DmoDbContext context, Guid boquilhasId)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT bq_id IS NULL, tool_id FROM boquilhas WHERE boquilhas_id = @id";
        command.Parameters.Add(new NpgsqlParameter("id", boquilhasId));
        await context.Database.OpenConnectionAsync();

        try
        {
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            return (reader.GetBoolean(0), reader.GetGuid(1));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static async Task<(Guid BqId, Guid JobOnId)> QueryLinkAsync(DmoDbContext context, Guid boquilhasId)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT b.bq_id, c.jobon_id
              FROM boquilhas b
              JOIN bq_contexts c ON c.bq_id = b.bq_id
             WHERE b.boquilhas_id = @id
            """;
        command.Parameters.Add(new NpgsqlParameter("id", boquilhasId));
        await context.Database.OpenConnectionAsync();

        try
        {
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            return (reader.GetGuid(0), reader.GetGuid(1));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static async Task<Guid?> QueryRepairerAsync(DmoDbContext context, Guid movementId)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT repairer_id FROM boquilha_movements WHERE movement_id = @id";
        command.Parameters.Add(new NpgsqlParameter("id", movementId));
        await context.Database.OpenConnectionAsync();

        try
        {
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            return reader.IsDBNull(0) ? null : reader.GetGuid(0);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static Task ExecuteAsync(
        DmoDbContext context,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        var rawParameters = parameters
            .Select(pair => new NpgsqlParameter(pair.Name, pair.Value))
            .Cast<object>()
            .ToArray();

        return context.Database.ExecuteSqlRawAsync(sql, rawParameters);
    }

    private static Task CleanupAsync(DmoDbContext context, string token)
    {
        // The token appears inside every reference/company-number/repairer-name this test creates;
        // the CONTAINS match (LIKE '%token%') guarantees the rows of THIS test are the only ones
        // removed. FK-safe order: audit → movements → registers → assignments → bq_contexts →
        // job_ons → tools → repairers → users.
        return context.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM boquilha_movement_audit
             WHERE movement_id IN (
                   SELECT movement_id FROM boquilha_movements
                    WHERE boquilhas_id IN (
                          SELECT boquilhas_id FROM boquilhas
                           WHERE created_by_user_id IN (
                                 SELECT user_id FROM users WHERE company_number LIKE CONCAT('%', {0}, '%'))
                              OR tool_id IN (SELECT tool_id FROM tools WHERE reference LIKE CONCAT('%', {0}, '%'))));
            DELETE FROM boquilha_movements
             WHERE boquilhas_id IN (
                   SELECT boquilhas_id FROM boquilhas
                    WHERE created_by_user_id IN (
                          SELECT user_id FROM users WHERE company_number LIKE CONCAT('%', {0}, '%'))
                       OR tool_id IN (SELECT tool_id FROM tools WHERE reference LIKE CONCAT('%', {0}, '%')));
            DELETE FROM boquilhas
             WHERE created_by_user_id IN (
                   SELECT user_id FROM users WHERE company_number LIKE CONCAT('%', {0}, '%'))
                OR tool_id IN (SELECT tool_id FROM tools WHERE reference LIKE CONCAT('%', {0}, '%'));
            DELETE FROM machine_repairer_assignments
             WHERE repairer_id IN (SELECT repairer_id FROM repairers WHERE name LIKE CONCAT('%', {0}, '%'));
            DELETE FROM bq_contexts
             WHERE jobon_id IN (SELECT jobon_id FROM job_ons WHERE reference LIKE CONCAT('%', {0}, '%'));
            DELETE FROM job_ons WHERE reference LIKE CONCAT('%', {0}, '%');
            DELETE FROM tools WHERE reference LIKE CONCAT('%', {0}, '%');
            DELETE FROM repairers WHERE name LIKE CONCAT('%', {0}, '%');
            DELETE FROM users WHERE company_number LIKE CONCAT('%', {0}, '%');
            """,
            token);
    }
}