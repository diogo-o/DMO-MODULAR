using DMO.Application.ControloCreate;
using DMO.Application.Persistence;
using DMO.Domain.Controlo;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Controlo;
using DMO.IntegrationTests.ControloCreate;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (row identifiers
// derived from a fresh Guid) against a disposable database. Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P2-T05 post-closure correction — env-gated integration test — the glass-density settings
/// store over the disposable PostgreSQL database: the seeded initial state, the version-guarded
/// per-processo writes (exact <c>numeric(18,4)</c> values, stale → <c>StaleVersion</c>, the
/// other processo untouched) and the service-level refusals.
/// </summary>
/// <remarks>
/// Authority: correction contract §5.1/§5.2 (table + seeds), §5.3 (PUT behavior), §19-style
/// concurrency (stale-version, nothing written) and §5.7 rows U/I/DB. Every row is
/// <c>[SkippableFact]</c> behind <see cref="PersistenceTestDatabase.SkipIfNotConfigured"/>.</remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class GlassDensitySettingsRepositoryIntegrationTests
{
    /// <summary>
    /// GD-DB1 — the real repository reads the seeded authoritative state: exactly the two rows,
    /// NNPB 2.4027 / PS 2.4231 at version 1, in canonical order.
    /// </summary>
    [SkippableFact]
    public async Task GD_DB1_TheSeededAuthoritativeStateIsReadable()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);
        await GlassDensityTestState.RestoreAsync(context);

        var repository = new GlassDensitySettingsRepository(context);

        var list = await repository.ListAsync(CancellationToken.None);
        Assert.Equal(new[] { "NNPB", "PS" }, list.Select(setting => setting.Processo));
        Assert.All(list, setting => Assert.Equal(1, setting.Version));
        Assert.Equal(2.4027m, list[0].DensityGCm3);
        Assert.Equal(2.4231m, list[1].DensityGCm3);

        var nnpb = await repository.GetByProcessoAsync("NNPB", CancellationToken.None);
        Assert.NotNull(nnpb);
        Assert.Equal(2.4027m, nnpb!.DensityGCm3);
        Assert.Equal(1, nnpb.Version);

        Assert.Null(await repository.GetByProcessoAsync("TOOL", CancellationToken.None));
    }

    /// <summary>
    /// GD-DB2 — the version-guarded write: an update bumps the version exactly once and preserves
    /// the exact 4-dp value; the OTHER processo's row keeps its value AND version (per-processo
    /// independence at the DB); a stale write throws the typed concurrency conflict and the row
    /// keeps the newer state.
    /// </summary>
    [SkippableFact]
    public async Task GD_DB2_UpdatesAreVersionGuardedAndPerProcessoIndependent()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);
        await GlassDensityTestState.RestoreAsync(context);

        try
        {
            var repository = new GlassDensitySettingsRepository(context);

            var current = await repository.GetByProcessoAsync("NNPB", CancellationToken.None);
            var updated = await repository.UpdatedAsync(
                current! with { DensityGCm3 = 2.4099m },
                CancellationToken.None);
            Assert.Equal(2.4099m, updated.DensityGCm3);
            Assert.Equal(2, updated.Version);

            // PS untouched: same value, same version.
            var ps = await repository.GetByProcessoAsync("PS", CancellationToken.None);
            Assert.Equal(2.4231m, ps!.DensityGCm3);
            Assert.Equal(1, ps.Version);

            // The stale write (observed version 1, current version 2) throws and writes nothing.
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repository.UpdatedAsync(
                current! with { DensityGCm3 = 2.5m },
                CancellationToken.None));

            var after = await repository.GetByProcessoAsync("NNPB", CancellationToken.None);
            Assert.Equal(2.4099m, after!.DensityGCm3);
            Assert.Equal(2, after.Version);
        }
        finally
        {
            await GlassDensityTestState.RestoreAsync(context);
        }
    }

    /// <summary>
    /// GD-DB3 — the service-level refusals over the real repositories: an update with a stale
    /// version returns <c>SettingsResult.Refused</c> (StaleVersion) and nothing is written; a
    /// non-canonical processo or non-positive density is a validation failure with exactly the
    /// correction tokens.
    /// </summary>
    [SkippableFact]
    public async Task GD_DB3_ServiceLevelRefusalsOverTheRealRepositories()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);
        await GlassDensityTestState.RestoreAsync(context);

        try
        {
            var service = new ControloDefinicoesService(
                new PdfDirectorySettingsRepository(context),
                new EmailListRepository(context),
                new EmailTemplateRepository(context),
                new GlassDensitySettingsRepository(context),
                new FixedPdfDirectoryProbe());

            // Validation failures: exact tokens, nothing written.
            var unknown = Assert.IsType<SettingsResult.ValidationFailed>(await service.UpdateGlassDensityAsync(
                new UpdateGlassDensityCommand("TOOL", 2.5m, 1),
                CancellationToken.None));
            Assert.Equal(new[] { ControloDefinicoesValidationErrors.ProcessoUnknown }, unknown.Errors);

            var notPositive = Assert.IsType<SettingsResult.ValidationFailed>(await service.UpdateGlassDensityAsync(
                new UpdateGlassDensityCommand("NNPB", 0m, 1),
                CancellationToken.None));
            Assert.Equal(new[] { ControloDefinicoesValidationErrors.DensityNotPositive }, notPositive.Errors);

            // Successful update: version 2, exact value.
            var updated = Assert.IsType<SettingsResult.GlassDensityUpdated>(
                await service.UpdateGlassDensityAsync(
                    new UpdateGlassDensityCommand("NNPB", 2.4099m, 1),
                    CancellationToken.None));
            Assert.Equal("NNPB", updated.Processo);
            Assert.Equal(2.4099m, updated.DensityGCm3);
            Assert.Equal(2, updated.Version);

            // Stale version: typed refusal, the newer value survives.
            var stale = Assert.IsType<SettingsResult.Refused>(await service.UpdateGlassDensityAsync(
                new UpdateGlassDensityCommand("NNPB", 2.5m, 1),
                CancellationToken.None));
            Assert.Equal(SettingsRefusalReason.StaleVersion, stale.Reason);

            var listed = Assert.IsType<SettingsResult.GlassDensitiesFound>(
                await service.ListGlassDensitiesAsync(CancellationToken.None)).GlassDensities;
            Assert.Equal(2, listed.Count);
            Assert.Contains(listed, setting => setting.Processo == "NNPB" && setting.DensityGCm3 == 2.4099m && setting.Version == 2);
            Assert.Contains(listed, setting => setting.Processo == "PS" && setting.DensityGCm3 == 2.4231m && setting.Version == 1);
        }
        finally
        {
            await GlassDensityTestState.RestoreAsync(context);
        }
    }
}