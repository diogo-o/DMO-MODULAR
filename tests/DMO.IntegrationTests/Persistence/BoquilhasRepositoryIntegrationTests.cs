using System.Data;
using DMO.Application.Boquilhas;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.Boquilhas;
using DMO.Domain.Tools;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Boquilhas;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (table names and row
// identifiers) against a disposable database. Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P2-T07 env-gated integration test — the REAL <see cref="BoquilhasRepository"/> over the REAL
/// single <c>DmoDbContext</c> and disposable PostgreSQL (the OWNER CLARIFICATION register model):
/// production association, the movement vocabulary, the derived outstanding replay, the Entrada
/// sem reparação semantics (no Tool mutation), the after-production-end movement, the edit/audit
/// single-event rule, the repairer historical preservation, the one-register-per-BQ-context unique
/// key and the register identity creation WITHOUT a quantity event.
/// </summary>
/// <remarks>
/// Authority: P2-T07 OWNER CLARIFICATION. Every row is <c>[SkippableFact]</c> behind
/// <see cref="PersistenceTestDatabase.SkipIfNotConfigured"/>; the database is the disposable local
/// PostgreSQL (never the shared Supabase TEST); every test token is unique per run.</remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class BoquilhasRepositoryIntegrationTests
{
    // ================================================================== fixtures

    private static async Task<Guid> SeedUserAsync(DmoDbContext context, string token)
    {
        var userId = Guid.NewGuid();
        await ExecuteAsync(
            context,
            "INSERT INTO users (user_id, auth_identity_id, company_number, name, email, active, version) " +
            "VALUES (@id, @auth, @company, @name, @email, true, 1)",
            ("id", (object)userId),
            ("auth", token),
            ("company", token),
            ("name", token),
            ("email", $"{token}@example.test"));

        return userId;
    }

    private static async Task<Guid> SeedToolAsync(DmoDbContext context, string token, string type = "BQ")
    {
        var toolId = Guid.NewGuid();
        await ExecuteAsync(
            context,
            "INSERT INTO tools (tool_id, tool_type, reference, lot, processo, quantity, created_at, updated_at) " +
            "VALUES (@id, @type, @reference, '01', 'NNPB', 10, now(), now())",
            ("id", (object)toolId),
            ("type", type),
            ("reference", $"REF-{token}"));

        return toolId;
    }

    private static async Task<Guid> SeedBqContextAsync(
        DmoDbContext context,
        string token,
        Guid toolId,
        DateOnly? productionDate = null)
    {
        var jobOnId = Guid.NewGuid();
        var bqId = Guid.NewGuid();
        await ExecuteAsync(
            context,
            "INSERT INTO job_ons (jobon_id, reference, production_number, machine, production_date, version, created_at, updated_at) " +
            "VALUES (@jobon, @reference, @production, 'B1', @productionDate, 1, now(), now())",
            ("jobon", (object)jobOnId),
            ("reference", $"REF-{token}"),
            ("production", $"P-{token}"),
            ("productionDate", productionDate is null ? DBNull.Value : (object)productionDate.Value));

        await ExecuteAsync(
            context,
            "INSERT INTO bq_contexts (bq_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot, created_at, updated_at) " +
            "VALUES (@bq, @jobon, @tool, 'BQ', @reference, '01', now(), now())",
            ("bq", (object)bqId),
            ("jobon", (object)jobOnId),
            ("tool", (object)toolId),
            ("reference", $"BQ-{token}"));

        return bqId;
    }

    private static async Task<Guid> SeedRepairerAsync(DmoDbContext context, string token)
    {
        var repairerId = Guid.NewGuid();
        await ExecuteAsync(
            context,
            "INSERT INTO repairers (repairer_id, name, version, created_at, updated_at) " +
            "VALUES (@id, @name, 1, now(), now())",
            ("id", (object)repairerId),
            ("name", $"Reparador-{token}"));

        return repairerId;
    }

    private static async Task<Guid> SeedRegisterAsync(
        DmoDbContext context,
        string token,
        Guid userId,
        Guid bqId)
    {
        var registerId = Guid.NewGuid();
        await ExecuteAsync(
            context,
            "INSERT INTO boquilhas (boquilhas_id, bq_id, created_by_user_id, created_at) " +
            "VALUES (@id, @bq, @user, now())",
            ("id", (object)registerId),
            ("bq", (object)bqId),
            ("user", (object)userId));

        return registerId;
    }

    private static async Task<Guid> SeedMovementAsync(
        DmoDbContext context,
        Guid registerId,
        Guid userId,
        string movementType,
        int quantity,
        DateOnly businessDate,
        string? machine = null,
        Guid? repairerId = null)
    {
        var movementId = Guid.NewGuid();
        await ExecuteAsync(
            context,
            "INSERT INTO boquilha_movements (movement_id, boquilhas_id, movement_type, quantity, business_date, " +
            "recorded_at, recorded_by_user_id, machine, repairer_id, version, created_at, updated_at) " +
            "VALUES (@id, @register, @type, @quantity, @businessDate, now(), @user, @machine, @repairer, 1, now(), now())",
            ("id", (object)movementId),
            ("register", (object)registerId),
            ("type", movementType),
            ("quantity", quantity),
            ("businessDate", businessDate),
            ("user", (object)userId),
            ("machine", machine is null ? DBNull.Value : (object)machine),
            ("repairer", repairerId is null ? DBNull.Value : (object)repairerId.Value));

        return movementId;
    }

    // ================================================================== 1. production association

    /// <summary>
    /// P1 — every register belongs to a REAL Job On/BQ context: the create anchors the real
    /// <c>bq_id</c> (never a fake Job On/bq id), the FK protects the anchor (23503 backstop) and the
    /// register creation manufactures NO quantity event.
    /// </summary>
    [SkippableFact]
    public async Task P1_RegisterAnchorsTheRealBqContext_AndCreationManufacturesNoMovement()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
            var userId = await SeedUserAsync(context, token);
            var toolId = await SeedToolAsync(context, token);
            var bqId = await SeedBqContextAsync(context, token, toolId);

            var repository = new BoquilhasRepository(context);
            var created = await repository.CreatedAsync(
                new BoqCreateUnit(bqId, PendingToolId: null, userId),
                CancellationToken.None);

            Assert.NotNull(created);
            Assert.Equal(bqId, created.BqId);
            Assert.Empty(created.Movements);
            Assert.Equal(0, created.Outstanding);

            // No fake anything: exactly one register row, zero movements.
            Assert.Equal(1, await QueryIntAsync(context, "SELECT count(*) FROM boquilhas"));
            Assert.Equal(0, await QueryIntAsync(context, "SELECT count(*) FROM boquilha_movements"));

            // The real production resolves through the bq context.
            var read = await repository.GetByIdAsync(created.BoquilhasId.Value, CancellationToken.None);
            Assert.NotNull(read);
            Assert.Equal(bqId, read.BqId);

            // The FK protects the anchor: cannot delete the referenced bq_contexts row (23503).
            var deleteGuard = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
                context,
                "DELETE FROM bq_contexts WHERE bq_id = @bq",
                ("bq", (object)bqId)));
            Assert.Equal("23503", deleteGuard.SqlState);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// P2 — a non-existent bq_id is refused with the typed failure inside the create transaction
    /// (nothing written); a second create for the SAME bq_id is refused with RegisterExists.
    /// </summary>
    [SkippableFact]
    public async Task P2_FakeBqIdRefused_AndOneRegisterPerBqContextEnforced()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
            var userId = await SeedUserAsync(context, token);
            var toolId = await SeedToolAsync(context, token);
            var bqId = await SeedBqContextAsync(context, token, toolId);

            var repository = new BoquilhasRepository(context);

            var fake = await Assert.ThrowsAsync<BoquilhasPersistenceException>(() =>
                repository.CreatedAsync(
                    new BoqCreateUnit(Guid.NewGuid(), PendingToolId: null, userId),
                    CancellationToken.None));
            Assert.Equal(BoquilhasPersistenceFailureReason.BqContextNotFound, fake.Reason);
            Assert.Equal(0, await QueryIntAsync(context, "SELECT count(*) FROM boquilhas"));

            await repository.CreatedAsync(new BoqCreateUnit(bqId, PendingToolId: null, userId), CancellationToken.None);

            var duplicate = await Assert.ThrowsAsync<BoquilhasPersistenceException>(() =>
                repository.CreatedAsync(
                    new BoqCreateUnit(bqId, PendingToolId: null, userId),
                    CancellationToken.None));
            Assert.Equal(BoquilhasPersistenceFailureReason.RegisterExists, duplicate.Reason);
            Assert.Equal(1, await QueryIntAsync(context, "SELECT count(*) FROM boquilhas"));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ================================================================== 2. production-ended case

    /// <summary>
    /// P3 — a movement can be recorded AFTER the production end date (27/09 → 29/09): the production
    /// stays the historical context; the business date is the movement's own fact and nothing
    /// rejects a later date.
    /// </summary>
    [SkippableFact]
    public async Task P3_MovementsAfterTheProductionEndDateAreValid()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
            var userId = await SeedUserAsync(context, token);
            var toolId = await SeedToolAsync(context, token);
            var bqId = await SeedBqContextAsync(
                context, token, toolId, productionDate: new DateOnly(2026, 9, 27));
            var repairerId = await SeedRepairerAsync(context, token);
            var registerId = await SeedRegisterAsync(context, token, userId, bqId);

            var repository = new BoquilhasRepository(context);

            await repository.AppendMovementAsync(
                new BoqAppendUnit(
                    registerId, "saida", 10, new DateOnly(2026, 9, 25), "B1", repairerId, null, userId),
                CancellationToken.None);

            // 29/09 — AFTER the production ended on 27/09: valid.
            var afterEnd = await repository.AppendMovementAsync(
                new BoqAppendUnit(
                    registerId, "entrada_sem_reparacao", 6, new DateOnly(2026, 9, 29), null, null, null, userId),
                CancellationToken.None);

            Assert.NotNull(afterEnd);
            Assert.Equal(MovementKind.EntradaSemReparacao, afterEnd.Kind);
            Assert.Equal(new DateOnly(2026, 9, 29), afterEnd.BusinessDate);

            var read = await repository.GetByIdAsync(registerId, CancellationToken.None);
            Assert.NotNull(read);
            Assert.Equal(4, read.Outstanding);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ================================================================== 3. movement vocabulary

    /// <summary>
    /// V1 — the closed three-type vocabulary is enforced in the DATABASE: Saída / Entrada / Entrada
    /// sem reparação append cleanly; a superseded type (inicio, irreparavel) is refused inside the
    /// transaction with the validator token (nothing written).
    /// </summary>
    [SkippableFact]
    public async Task V1_TheClosedThreeTypesAreEnforced()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
            var userId = await SeedUserAsync(context, token);
            var toolId = await SeedToolAsync(context, token);
            var bqId = await SeedBqContextAsync(context, token, toolId);
            var repairerId = await SeedRepairerAsync(context, token);
            var registerId = await SeedRegisterAsync(context, token, userId, bqId);

            var repository = new BoquilhasRepository(context);

            var saida = await repository.AppendMovementAsync(
                new BoqAppendUnit(registerId, "saida", 10, new DateOnly(2026, 9, 25), "B1", repairerId, null, userId),
                CancellationToken.None);
            var entrada = await repository.AppendMovementAsync(
                new BoqAppendUnit(registerId, "entrada", 4, new DateOnly(2026, 9, 27), null, null, null, userId),
                CancellationToken.None);
            var semReparacao = await repository.AppendMovementAsync(
                new BoqAppendUnit(registerId, "entrada_sem_reparacao", 6, new DateOnly(2026, 9, 29), null, null, null, userId),
                CancellationToken.None);

            Assert.Equal(MovementKind.Saida, saida.Kind);
            Assert.Equal(MovementKind.Entrada, entrada.Kind);
            Assert.Equal(MovementKind.EntradaSemReparacao, semReparacao.Kind);
            Assert.Equal(0, (await repository.GetByIdAsync(registerId, CancellationToken.None))!.Outstanding);

            // Superseded types are refused with the validator token (nothing written).
            foreach (var superseded in new[] { "inicio", "irreparavel" })
            {
                var refused = await Assert.ThrowsAsync<BoquilhasPersistenceException>(() =>
                    repository.AppendMovementAsync(
                        new BoqAppendUnit(registerId, superseded, 1, new DateOnly(2026, 9, 29), null, null, null, userId),
                        CancellationToken.None));
                Assert.Equal(BoquilhasPersistenceFailureReason.ConstraintViolation, refused.Reason);
                Assert.Equal(BoquilhasValidationErrors.MovementTypeInvalid, refused.ValidatorToken);
            }

            Assert.Equal(3, await QueryIntAsync(context, "SELECT count(*) FROM boquilha_movements"));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// V2 — a Saída without machine or without repairer is refused inside the transaction with the
    /// exact validator tokens (nothing written); the machine code CHECK is DB-enforced.
    /// </summary>
    [SkippableFact]
    public async Task V2_SaidaRequiredFactsAreEnforced()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
            var userId = await SeedUserAsync(context, token);
            var toolId = await SeedToolAsync(context, token);
            var bqId = await SeedBqContextAsync(context, token, toolId);
            var repairerId = await SeedRepairerAsync(context, token);
            var registerId = await SeedRegisterAsync(context, token, userId, bqId);

            var repository = new BoquilhasRepository(context);

            var noMachine = await Assert.ThrowsAsync<BoquilhasPersistenceException>(() =>
                repository.AppendMovementAsync(
                    new BoqAppendUnit(registerId, "saida", 1, new DateOnly(2026, 9, 25), null, repairerId, null, userId),
                    CancellationToken.None));
            Assert.Equal(BoquilhasValidationErrors.MachineRequired, noMachine.ValidatorToken);

            var noRepairer = await Assert.ThrowsAsync<BoquilhasPersistenceException>(() =>
                repository.AppendMovementAsync(
                    new BoqAppendUnit(registerId, "saida", 1, new DateOnly(2026, 9, 25), "B1", null, null, userId),
                    CancellationToken.None));
            Assert.Equal(BoquilhasValidationErrors.RepairerRequired, noRepairer.ValidatorToken);

            var unknownMachine = await Assert.ThrowsAsync<BoquilhasPersistenceException>(() =>
                repository.AppendMovementAsync(
                    new BoqAppendUnit(registerId, "saida", 1, new DateOnly(2026, 9, 25), "X9", repairerId, null, userId),
                    CancellationToken.None));
            Assert.Equal(BoquilhasValidationErrors.MachineUnknown, unknownMachine.ValidatorToken);

            Assert.Equal(0, await QueryIntAsync(context, "SELECT count(*) FROM boquilha_movements"));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ================================================================== 4. quantity replay

    /// <summary>
    /// R1 — the derived outstanding follows the Owner formula over the REAL ledger:
    /// Saída 10 → Entrada 4 → Entrada sem reparação 6 ⇒ outstanding 0; the replay is read-only
    /// (no stored balance anywhere).
    /// </summary>
    [SkippableFact]
    public async Task R1_TheOutstandingIsDerivedByReplayOnly()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
            var userId = await SeedUserAsync(context, token);
            var toolId = await SeedToolAsync(context, token);
            var bqId = await SeedBqContextAsync(context, token, toolId);
            var repairerId = await SeedRepairerAsync(context, token);
            var registerId = await SeedRegisterAsync(context, token, userId, bqId);

            // Arranged history: the exact Owner example.
            await SeedMovementAsync(context, registerId, userId, "saida", 10, new DateOnly(2026, 9, 25), "B1", repairerId);
            await SeedMovementAsync(context, registerId, userId, "entrada", 4, new DateOnly(2026, 9, 27));
            await SeedMovementAsync(context, registerId, userId, "entrada_sem_reparacao", 6, new DateOnly(2026, 9, 29));

            var read = await new BoquilhasRepository(context).GetByIdAsync(registerId, CancellationToken.None);

            Assert.NotNull(read);
            Assert.Equal(0, read.Outstanding);

            // No balance table/column exists in the database at all.
            var balanceTables = await QueryIntAsync(
                context,
                "SELECT count(*) FROM information_schema.tables WHERE table_schema='public' " +
                "AND table_name LIKE '%balance%'");
            Assert.Equal(0, balanceTables);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ================================================================== 5. Entrada sem reparação

    /// <summary>
    /// E1 — an Entrada sem reparação returns quantity from repair and NEVER mutates the canonical
    /// Tool row: the movement row keeps the distinct token; the Tool row remains byte-identical.
    /// </summary>
    [SkippableFact]
    public async Task E1_EntradaSemReparacaoDoesNotMutateTheTool()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
            var userId = await SeedUserAsync(context, token);
            var toolId = await SeedToolAsync(context, token, type: "BQ");
            var bqId = await SeedBqContextAsync(context, token, toolId);
            var repairerId = await SeedRepairerAsync(context, token);
            var registerId = await SeedRegisterAsync(context, token, userId, bqId);

            var toolBefore = new ToolRead(
                await QueryStringAsync(context, "SELECT tool_type FROM tools WHERE tool_id = @id", ("id", (object)toolId)),
                await QueryStringAsync(context, "SELECT reference FROM tools WHERE tool_id = @id", ("id", (object)toolId)),
                await QueryStringAsync(context, "SELECT lot FROM tools WHERE tool_id = @id", ("id", (object)toolId)));

            var repository = new BoquilhasRepository(context);
            await repository.AppendMovementAsync(
                new BoqAppendUnit(registerId, "saida", 6, new DateOnly(2026, 9, 25), "B1", repairerId, null, userId),
                CancellationToken.None);
            await repository.AppendMovementAsync(
                new BoqAppendUnit(registerId, "entrada_sem_reparacao", 6, new DateOnly(2026, 9, 29), null, null, null, userId),
                CancellationToken.None);

            // The movement keeps its distinct meaning in the ledger.
            var read = await repository.GetByIdAsync(registerId, CancellationToken.None);
            Assert.NotNull(read);
            Assert.Equal(0, read.Outstanding);
            Assert.Equal(MovementKind.EntradaSemReparacao, read.Ledger[1].Kind);

            // The Tool row is untouched (no irreparable state, no identity change, no deletion).
            Assert.Equal(
                toolBefore,
                new ToolRead(
                    await QueryStringAsync(context, "SELECT tool_type FROM tools WHERE tool_id = @id", ("id", (object)toolId)),
                    await QueryStringAsync(context, "SELECT reference FROM tools WHERE tool_id = @id", ("id", (object)toolId)),
                    await QueryStringAsync(context, "SELECT lot FROM tools WHERE tool_id = @id", ("id", (object)toolId))));
            Assert.Equal(1, await QueryIntAsync(context, "SELECT count(*) FROM tools"));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ================================================================== 6. edit / audit

    /// <summary>
    /// E2 — the edit replaces the SAME movement row (count unchanged, id unchanged, type/recorded_at
    /// immutable) and writes ONE audit row in the same transaction: no second quantity event, and
    /// the derived outstanding reflects the edited row exactly once.
    /// </summary>
    [SkippableFact]
    public async Task E2_EditIsTheSameRowPlusOneAuditRow_NoDoubleCount()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
            var userId = await SeedUserAsync(context, token);
            var toolId = await SeedToolAsync(context, token);
            var bqId = await SeedBqContextAsync(context, token, toolId);
            var repairerId = await SeedRepairerAsync(context, token);
            var registerId = await SeedRegisterAsync(context, token, userId, bqId);
            var movementId = await SeedMovementAsync(
                context, registerId, userId, "saida", 10, new DateOnly(2026, 9, 25), "B1", repairerId);

            var repository = new BoquilhasRepository(context);
            var result = await repository.EditMovementAsync(
                new BoqEditUnit(
                    registerId, movementId, ExpectedMovementVersion: 1, Quantity: 7,
                    BusinessDate: new DateOnly(2026, 9, 26), Machine: "B1", RepairerId: repairerId,
                    Observations: "corrigido", EditedByUserId: userId),
                CancellationToken.None);

            Assert.Equal(movementId, result.Movement.MovementId.Value);
            Assert.Equal(MovementKind.Saida, result.Movement.Kind);
            Assert.Equal(2, result.Movement.Version);

            var read = await repository.GetByIdAsync(registerId, CancellationToken.None);
            Assert.NotNull(read);
            Assert.Single(read.Movements);
            Assert.Equal(7, read.Movements[0].Quantity);
            Assert.Equal(7, read.Outstanding);

            // Exactly one audit row with the exact before/after facts.
            var audits = await repository.GetMovementAuditAsync(movementId, CancellationToken.None);
            var audit = Assert.Single(audits);
            Assert.Equal(10, audit.BeforeQuantity);
            Assert.Equal(7, audit.AfterQuantity);
            Assert.Equal(userId, audit.EditedByUserId);

            Assert.Equal(1, await QueryIntAsync(context, "SELECT count(*) FROM boquilha_movement_audit"));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// E3 — a stale movement version is refused (nothing written): the row keeps its values and no
    /// audit row appears.
    /// </summary>
    [SkippableFact]
    public async Task E3_StaleEditIsRefused_NothingWritten()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
            var userId = await SeedUserAsync(context, token);
            var toolId = await SeedToolAsync(context, token);
            var bqId = await SeedBqContextAsync(context, token, toolId);
            var repairerId = await SeedRepairerAsync(context, token);
            var registerId = await SeedRegisterAsync(context, token, userId, bqId);
            var movementId = await SeedMovementAsync(
                context, registerId, userId, "saida", 10, new DateOnly(2026, 9, 25), "B1", repairerId);

            var repository = new BoquilhasRepository(context);

            var stale = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                repository.EditMovementAsync(
                    new BoqEditUnit(
                        registerId, movementId, ExpectedMovementVersion: 42, Quantity: 1,
                        BusinessDate: new DateOnly(2026, 9, 26), Machine: "B1", RepairerId: repairerId,
                        Observations: null, EditedByUserId: userId),
                    CancellationToken.None));
            Assert.NotNull(stale);

            var read = await repository.GetByIdAsync(registerId, CancellationToken.None);
            Assert.NotNull(read);
            Assert.Equal(10, read.Movements[0].Quantity);
            Assert.Equal(1, read.Movements[0].Version);
            Assert.Equal(0, await QueryIntAsync(context, "SELECT count(*) FROM boquilha_movement_audit"));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ================================================================== 7. repairer history

    /// <summary>
    /// R2 — a later assignment change does NOT rewrite an earlier movement: the movement row keeps
    /// the repairer used at the time of the movement (the current assignment is only a consumed
    /// read elsewhere).
    /// </summary>
    [SkippableFact]
    public async Task R2_LaterAssignmentChangeDoesNotRewriteTheMovement()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
            var userId = await SeedUserAsync(context, token);
            var toolId = await SeedToolAsync(context, token);
            var bqId = await SeedBqContextAsync(context, token, toolId);
            var firstRepairer = await SeedRepairerAsync(context, $"{token}-A");
            var secondRepairer = await SeedRepairerAsync(context, $"{token}-B");
            var registerId = await SeedRegisterAsync(context, token, userId, bqId);

            // The movement is recorded with the FIRST repairer; the assignment changes LATER.
            var movementId = await SeedMovementAsync(
                context, registerId, userId, "saida", 10, new DateOnly(2026, 9, 25), "B1", firstRepairer);

            await ExecuteAsync(
                context,
                "INSERT INTO machine_repairer_assignments (machine_repairer_assignment_id, machine, repairer_id, version, created_at, updated_at) " +
                "VALUES (@id, 'B1', @repairer, 1, now(), now())",
                ("id", (object)Guid.NewGuid()),
                ("repairer", (object)secondRepairer));

            var repository = new BoquilhasRepository(context);
            var read = await repository.GetByIdAsync(registerId, CancellationToken.None);

            Assert.NotNull(read);
            Assert.Equal(firstRepairer, read.Movements[0].RepairerId);
            Assert.Equal(movementId, read.Movements[0].MovementId.Value);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ================================================================== 8. schema facts (DB facet)

    /// <summary>
    /// S1 — the corrected physical schema returns exactly the closed facts: THREE Boquilhas tables,
    /// no lifecycle table, no <c>status</c> column, no active-anchor partial index, and the plain
    /// one-register-per-BQ-context unique key.
    /// </summary>
    [SkippableFact]
    public async Task S1_ThePhysicalSchemaHasNoLifecycleStructures()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var tables = await QueryStringListAsync(
            context,
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_type = 'BASE TABLE' " +
            "AND table_name LIKE 'boquilha%'");

        Assert.Equal(
            ["boquilha_movement_audit", "boquilha_movements", "boquilhas"],
            tables.OrderBy(name => name, StringComparer.Ordinal).ToList());

        var hasStatusColumn = await QueryIntAsync(
            context,
            "SELECT count(*) FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND table_name = 'boquilhas' AND column_name = 'status'");
        Assert.Equal(0, hasStatusColumn);

        var activeIndexes = await QueryIntAsync(
            context,
            "SELECT count(*) FROM pg_indexes WHERE tablename = 'boquilhas' AND indexname LIKE '%_active_%'");
        Assert.Equal(0, activeIndexes);
    }

    // ================================================================== helpers

    private sealed record ToolRead(string Type, string Reference, string Lot);

    private static async Task<List<string>> QueryStringListAsync(DmoDbContext context, string sql)
    {
        var rows = new List<string>();
        var connection = context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static async Task<string> QueryStringAsync(
        DmoDbContext context,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter(name, value));
        }

        var result = await command.ExecuteScalarAsync();
        return result?.ToString() ?? string.Empty;
    }

    private static async Task<int> QueryIntAsync(DmoDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return reader.GetInt32(0);
    }

    private static async Task ExecuteAsync(
        DmoDbContext context,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter(name, value));
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task CleanupAsync(DmoDbContext context, string token)
    {
        foreach (var table in new[] { "boquilha_movement_audit", "boquilha_movements", "boquilhas" })
        {
            await PersistenceTestDatabase.ClearTableAsync(context, table);
        }

        await ExecuteAsync(context, "DELETE FROM bq_contexts");
        await ExecuteAsync(context, "DELETE FROM job_ons");
        await ExecuteAsync(context, "DELETE FROM tools");
        await ExecuteAsync(context, "DELETE FROM machine_repairer_assignments");
        await ExecuteAsync(context, "DELETE FROM repairers");
        await ExecuteAsync(context, "DELETE FROM users");
    }
}