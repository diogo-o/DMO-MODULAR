using System.Net;
using System.Text.Json;
using DMO.Application.Controlo.Pesos;
using DMO.Application.Controlo.Settings;
using DMO.Domain.Boquilhas;
using DMO.Domain.Tools;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DMO.IntegrationTests.Boquilhas;

/// <summary>
/// <c>Boquilhas > Definições</c> transport proofs (Owner clarification P2-T07 §34.3 / P2-T05
/// §31.3): the repairer register and the independent line/machine → repairer assignments now
/// belong to the Boquilhas surface (<c>/boquilhas/definicoes</c>) — the same physical tables, the
/// moved ownership/service/UI (no schema migration).
/// </summary>
/// <remarks>
/// <para>
/// Authority: P2-T07 OWNER CLARIFICATION §34.3 (ownership transfer; supersedes the affected
/// P2-T05/P2-T07 ownership wording). Shape rules unchanged as shape: name-only register (no delete
/// path), ONE independent assignment per machine (B1/B2/B3/C1/C2/C3 — changing one machine never
/// touches another), no grouping, current-state only; historical movement facts are frozen and a
/// default-repairer change never rewrites them.</para>
/// <para>
/// The real <see cref="BoquilhasDefinicoesService"/> runs over the controlled
/// <see cref="P2T07TestStore"/> (which implements the SAME <c>IRepairerRepository</c>/
/// <c>IMachineRepairerAssignmentRepository</c> contracts the physical tables back) — the transport
/// and the gates are proven here; the persistence is proven by the env-gated DB tests.</para>
/// </remarks>
public sealed class BoquilhasDefinicoesEndpointsTests
{
    private const string DefinicoesPath = "/boquilhas/definicoes";

    /// <summary>
    /// DEF1 — the repairer register round-trip on the Boquilhas surface: create → 201 with the
    /// real id, list → one item, rename → the SAME repairer_id with version 2, a stale
    /// rename → 409 <c>stale-version</c>, and a blank name → 400 <c>NAME_REQUIRED</c>. The
    /// Controlo surface no longer serves these (the moved gate).
    /// </summary>
    [Fact]
    public async Task DEF1_RepairerRegisterRoundTripRetainsTheIdAcrossTheRename()
    {
        var store = new P2T07TestStore();

        using var factory = P2T07TestHost.ForUser(P2T07TestHost.AllGranted(), store);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        Guid repairerId;
        using (var created = await P2T07TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{DefinicoesPath}/repairers",
                   P2T07TestHost.Json(new { name = "José" })))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var payload = await ReadJsonAsync(created);
            repairerId = payload.GetProperty("repairerId").GetGuid();
            Assert.NotEqual(Guid.Empty, repairerId);
            Assert.Equal(1, payload.GetProperty("version").GetInt32());
        }

        using (var list = await P2T07TestHost.GetAsync(client, $"{DefinicoesPath}/repairers"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);

            var payload = await ReadJsonAsync(list);
            var item = Assert.Single(payload.GetProperty("repairers").EnumerateArray().ToArray());
            Assert.Equal(repairerId, item.GetProperty("repairerId").GetGuid());
            Assert.Equal("José", item.GetProperty("name").GetString());
            Assert.Equal(1, item.GetProperty("version").GetInt32());
        }

        using (var renamed = await P2T07TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{DefinicoesPath}/repairers/{repairerId}",
                   P2T07TestHost.Json(new { expectedVersion = 1, name = "José Silva" })))
        {
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

            var payload = await ReadJsonAsync(renamed);
            Assert.Equal(repairerId, payload.GetProperty("repairerId").GetGuid());
            Assert.Equal(2, payload.GetProperty("version").GetInt32());
        }

        // A stale rename is refused with the typed token and writes nothing.
        using (var stale = await P2T07TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{DefinicoesPath}/repairers/{repairerId}",
                   P2T07TestHost.Json(new { expectedVersion = 1, name = "José Outra Vez" })))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            var payload = await ReadJsonAsync(stale);
            Assert.Equal("stale-version", payload.GetProperty("reason").GetString());
        }

        // A blank name is a validation failure, never a write.
        using (var blank = await P2T07TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{DefinicoesPath}/repairers",
                   P2T07TestHost.Json(new { name = "   " })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

            var payload = await ReadJsonAsync(blank);
            Assert.Equal("validation-failed", payload.GetProperty("reason").GetString());
            Assert.Contains(
                ControloDefinicoesValidationErrors.NameRequired,
                payload.GetProperty("errors").EnumerateArray().Select(error => error.GetString() ?? string.Empty));
        }

        // The moved gate: the Controlo surface no longer serves the repairer register.
        using var controloClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var moved = await P2T07TestHost.GetAsync(controloClient, "/controlo/create/definicoes/repairers");
        Assert.Equal(HttpStatusCode.NotFound, moved.StatusCode);
    }

    /// <summary>
    /// DEF2 (MAC7 relocated) — proves AC-E5 on the Boquilhas surface: the six machine assignments
    /// are always listed (B1…C3), one machine is set/changed/cleared INDEPENDENTLY (the other five
    /// are never touched), an unknown machine is <c>MACHINE_UNKNOWN</c>, a clear returns version 0,
    /// and an unrelated grant never satisfies a Boquilhas Definições write (server-side 403).
    /// </summary>
    [Fact]
    public async Task DEF2_MachineAssignmentsChangeOnlyTheTargetedMachine()
    {
        var store = new P2T07TestStore();
        Guid repairerId = store.SeedRepairer("José").RepairerId.Value;

        using var factory = P2T07TestHost.ForUser(P2T07TestHost.AllGranted(), store);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // The initial snapshot is ALL SIX machines, all unassigned.
        using (var initial = await P2T07TestHost.GetAsync(client, $"{DefinicoesPath}/machine-assignments"))
        {
            Assert.Equal(HttpStatusCode.OK, initial.StatusCode);

            var assignments = (await ReadJsonAsync(initial)).GetProperty("assignments");
            Assert.Equal(6, assignments.GetArrayLength());
            Assert.Equal(
                new[] { "B1", "B2", "B3", "C1", "C2", "C3" },
                assignments.EnumerateArray().Select(item => item.GetProperty("machine").GetString()));
            Assert.All(
                assignments.EnumerateArray(),
                item => Assert.Equal(JsonValueKind.Null, item.GetProperty("repairerId").ValueKind));
        }

        // Set B1 only.
        using (var setB1 = await P2T07TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{DefinicoesPath}/machine-assignments/B1",
                   P2T07TestHost.Json(new { repairerId, expectedVersion = (int?)null })))
        {
            Assert.Equal(HttpStatusCode.OK, setB1.StatusCode);

            var payload = await ReadJsonAsync(setB1);
            Assert.Equal("B1", payload.GetProperty("machine").GetString());
            Assert.Equal(1, payload.GetProperty("version").GetInt32());
        }

        // Set C3 independently; the other four machines stay untouched.
        using (var setC3 = await P2T07TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{DefinicoesPath}/machine-assignments/C3",
                   P2T07TestHost.Json(new { repairerId, expectedVersion = (int?)null })))
        {
            Assert.Equal(HttpStatusCode.OK, setC3.StatusCode);

            var payload = await ReadJsonAsync(setC3);
            Assert.Equal("C3", payload.GetProperty("machine").GetString());
            Assert.Equal(1, payload.GetProperty("version").GetInt32());
        }

        using (var after = await P2T07TestHost.GetAsync(client, $"{DefinicoesPath}/machine-assignments"))
        {
            var assignments = (await ReadJsonAsync(after)).GetProperty("assignments");

            var byMachine = assignments.EnumerateArray()
                .ToDictionary(item => item.GetProperty("machine").GetString()!, item => item);

            Assert.Equal(repairerId, byMachine["B1"].GetProperty("repairerId").GetGuid());
            Assert.Equal(repairerId, byMachine["C3"].GetProperty("repairerId").GetGuid());
            Assert.All(
                new[] { "B2", "B3", "C1", "C2" },
                machine => Assert.Equal(
                    JsonValueKind.Null,
                    byMachine[machine].GetProperty("repairerId").ValueKind));
        }

        // An unknown machine is refused with the typed token.
        using (var unknown = await P2T07TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{DefinicoesPath}/machine-assignments/B4",
                   P2T07TestHost.Json(new { repairerId, expectedVersion = (int?)null })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

            var payload = await ReadJsonAsync(unknown);
            Assert.Equal("validation-failed", payload.GetProperty("reason").GetString());
            Assert.Contains(
                ControloDefinicoesValidationErrors.MachineUnknown,
                payload.GetProperty("errors").EnumerateArray().Select(error => error.GetString() ?? string.Empty));
        }

        // The explicit clear of B1 returns version 0 (cleared semantics) and touches only B1.
        using (var cleared = await P2T07TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{DefinicoesPath}/machine-assignments/B1",
                   P2T07TestHost.Json(new { repairerId = (Guid?)null, expectedVersion = 1 })))
        {
            Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);

            var payload = await ReadJsonAsync(cleared);
            Assert.Equal("B1", payload.GetProperty("machine").GetString());
            Assert.Equal(0, payload.GetProperty("version").GetInt32());
        }

        using (var afterClear = await P2T07TestHost.GetAsync(client, $"{DefinicoesPath}/machine-assignments"))
        {
            var assignments = (await ReadJsonAsync(afterClear)).GetProperty("assignments");

            var byMachine = assignments.EnumerateArray()
                .ToDictionary(item => item.GetProperty("machine").GetString()!, item => item);

            Assert.Equal(JsonValueKind.Null, byMachine["B1"].GetProperty("repairerId").ValueKind);
            Assert.Equal(repairerId, byMachine["C3"].GetProperty("repairerId").GetGuid());
        }

        // The moved gate: an unrelated grant (job-on-view) is denied the Definições write
        // server-side (403), and ADMIN gains nothing.
        using (var siblingFactory = P2T07TestHost.ForUser(P2T07TestHost.UnrelatedOnly(), store))
        using (var siblingClient = siblingFactory.CreateClient())
        {
            using var denied = await P2T07TestHost.SendJsonAsync(
                siblingClient,
                HttpMethod.Put,
                $"{DefinicoesPath}/machine-assignments/C3",
                P2T07TestHost.Json(new { repairerId, expectedVersion = (int?)null }));

            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using (var adminFactory = P2T07TestHost.ForAdmin(P2T07TestHost.AllGranted(), store))
        using (var adminClient = adminFactory.CreateClient())
        {
            using var denied = await P2T07TestHost.SendJsonAsync(
                adminClient,
                HttpMethod.Put,
                $"{DefinicoesPath}/machine-assignments/C3",
                P2T07TestHost.Json(new { repairerId, expectedVersion = (int?)null }));

            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        // C3 is exactly the state a denied write must leave untouched.
        using (var final = await P2T07TestHost.GetAsync(client, $"{DefinicoesPath}/machine-assignments"))
        {
            var assignments = (await ReadJsonAsync(final)).GetProperty("assignments");
            var c3 = assignments.EnumerateArray().Single(item => item.GetProperty("machine").GetString() == "C3");
            Assert.Equal(repairerId, c3.GetProperty("repairerId").GetGuid());
            Assert.Equal(1, c3.GetProperty("version").GetInt32());
        }
    }

    /// <summary>
    /// DEF3 — the six assignments are INDEPENDENT (B1/B2/B3/C1/C2/C3): five machines keep their
    /// repairers while one is changed; changing the default repairer of a line NEVER rewrites the
    /// historical movement facts — the movement keeps the repairer used at the time.
    /// </summary>
    [Fact]
    public async Task DEF3_AssignmentsAreIndependent_AndChangesNeverRewriteHistoricalMovements()
    {
        var store = new P2T07TestStore();
        var repairerA = store.SeedRepairer("Reparador A").RepairerId.Value;
        var repairerB = store.SeedRepairer("Reparador B").RepairerId.Value;

        // A historical Saída on B1 was recorded with repairer A (frozen fact).
        var tool = store.SeedTool(ToolType.Bq, "5447T173", "LOTE-1");
        var (_, bqId) = store.SeedProductionWithBq("REF-1", "P1", tool.ToolId.Value);
        var register = store.SeedRegister(bqId, (MovementKind.Saida, 5, new DateOnly(2026, 9, 25), "B1", repairerA));

        using var factory = P2T07TestHost.ForUser(P2T07TestHost.AllGranted(), store);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Give every machine its own independent assignment.
        var machines = new[] { "B1", "B2", "B3", "C1", "C2", "C3" };
        foreach (var machine in machines)
        {
            using var set = await P2T07TestHost.SendJsonAsync(
                client,
                HttpMethod.Put,
                $"{DefinicoesPath}/machine-assignments/{machine}",
                P2T07TestHost.Json(new { repairerId = machine == "B1" ? repairerA : repairerB, expectedVersion = (int?)null }));
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        }

        // Change ONLY B1's default (B1 → repairer B); the other five keep theirs.
        using (var changed = await P2T07TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{DefinicoesPath}/machine-assignments/B1",
                   P2T07TestHost.Json(new { repairerId = repairerB, expectedVersion = 1 })))
        {
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        }

        using (var list = await P2T07TestHost.GetAsync(client, $"{DefinicoesPath}/machine-assignments"))
        {
            var assignments = (await ReadJsonAsync(list)).GetProperty("assignments");
            var byMachine = assignments.EnumerateArray()
                .ToDictionary(item => item.GetProperty("machine").GetString()!, item => item);

            Assert.Equal(repairerB, byMachine["B1"].GetProperty("repairerId").GetGuid());
            foreach (var machine in new[] { "B2", "B3", "C1", "C2", "C3" })
            {
                Assert.Equal(repairerB, byMachine[machine].GetProperty("repairerId").GetGuid());
            }
        }

        // The historical movement KEEPS repairer A: the ficha (and the store row) are untouched by
        // the default change — the movement freezes the repairer used at the time.
        Assert.Equal(repairerA, register.Ledger.Single().RepairerId);

        using var ficha = await P2T07TestHost.GetAsync(
            client, $"/boquilhas/registers/{register.BoquilhasId.Value}");
        Assert.Equal(HttpStatusCode.OK, ficha.StatusCode);
        var fichaPayload = await ReadJsonAsync(ficha);
        var movement = fichaPayload.GetProperty("ficha").GetProperty("movements")[0];
        Assert.Equal(repairerA, movement.GetProperty("repairerId").GetGuid());
    }

    // ---- arrangement helpers -------------------------------------------------------------

    /// <summary>Reads a JSON response body into a detached element.</summary>
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}