using DMO.Application.Persistence;
using DMO.Application.Templates;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Access;
using Microsoft.EntityFrameworkCore;

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P1-T03 env-gated integration test — <see cref="TemplateRepository"/> primitives: atomic
/// create/update of the Template + composition and the Template-row version as the
/// composition concurrency boundary.
/// </summary>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class TemplateRepositoryIntegrationTests
{
    [SkippableFact]
    public async Task CreateAndUpdate_PersistCompositionAtomically()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        try
        {
            var repository = new TemplateRepository(context);
            var moduleReader = new TemplateModuleRepository(context);

            // Create with two modules.
            var templateId = await repository.CreatedAsync(
                new Template(Guid.Empty, "Templates Reparações", LandingDestinationId: null, Version: 1),
                [
                    new TemplateModule(Guid.Empty, "module-core", 1),
                    new TemplateModule(Guid.Empty, "module-parts", 2),
                ],
                CancellationToken.None);

            var created = await repository.GetByIdAsync(templateId, CancellationToken.None);
            Assert.NotNull(created);
            Assert.Equal("Templates Reparações", created!.Name);
            Assert.Equal(1, created.Version);

            var modules = await moduleReader.GetByTemplateAsync(templateId, CancellationToken.None);
            Assert.Equal(
                new[] { "module-core", "module-parts" },
                modules.Select(module => module.ModuleId).ToArray());
            Assert.Equal(new[] { 1, 2 }, modules.Select(module => module.PresentationOrder).ToArray());

            // Update (version 1) replaces the composition atomically in the same transaction.
            var updated = new Template(templateId, "Templates Reparações V2", "dest-42", Version: 1);
            await repository.UpdatedAsync(
                updated,
                [new TemplateModule(templateId, "module-parts", 1), new TemplateModule(templateId, "module-tools", 2)],
                CancellationToken.None);

            var reloaded = await repository.GetByIdAsync(templateId, CancellationToken.None);
            Assert.Equal("Templates Reparações V2", reloaded!.Name);
            Assert.Equal("dest-42", reloaded.LandingDestinationId);
            Assert.Equal(2, reloaded.Version);

            var modulesAfter = await moduleReader.GetByTemplateAsync(templateId, CancellationToken.None);
            Assert.Equal(new[] { "module-parts", "module-tools" }, modulesAfter.Select(module => module.ModuleId).ToArray());

            // Delete with the observed version.
            await repository.DeleteAsync(templateId, expectedVersion: 2, CancellationToken.None);
            Assert.Null(await repository.GetByIdAsync(templateId, CancellationToken.None));
            Assert.Empty(await moduleReader.GetByTemplateAsync(templateId, CancellationToken.None));
        }
        finally
        {
            await PersistenceTestDatabase.CleanFoundationTablesAsync(context);
        }
    }

    [SkippableFact]
    public async Task StaleTemplateVersion_CompositionWrite_IsRejected_WithoutOverwrite()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        try
        {
            var repository = new TemplateRepository(context);
            var moduleReader = new TemplateModuleRepository(context);

            var templateId = await repository.CreatedAsync(
                new Template(Guid.Empty, "T", null, Version: 1),
                [new TemplateModule(Guid.Empty, "module-a", 1)],
                CancellationToken.None);

            // Advance the row to version 2.
            await repository.UpdatedAsync(
                new Template(templateId, "T2", null, Version: 1),
                [new TemplateModule(templateId, "module-a", 1), new TemplateModule(templateId, "module-b", 2)],
                CancellationToken.None);

            // A write carrying the stale observed version (1, now 2) is rejected as a typed
            // conflict before any change is applied.
            var conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                repository.UpdatedAsync(
                    new Template(templateId, "STALE", null, Version: 1),
                    [new TemplateModule(templateId, "module-z", 9)],
                    CancellationToken.None));

            Assert.Contains("concurrently", conflict.Message, StringComparison.OrdinalIgnoreCase);

            // No overwrite: name/version/composition are exactly as after the second write.
            var row = await repository.GetByIdAsync(templateId, CancellationToken.None);
            Assert.Equal("T2", row!.Name);
            Assert.Equal(2, row.Version);
            Assert.Equal(
                new[] { "module-a", "module-b" },
                (await moduleReader.GetByTemplateAsync(templateId, CancellationToken.None))
                    .Select(module => module.ModuleId).ToArray());

            // Stale delete is rejected too.
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                repository.DeleteAsync(templateId, expectedVersion: 1, CancellationToken.None));
            Assert.NotNull(await repository.GetByIdAsync(templateId, CancellationToken.None));
        }
        finally
        {
            await PersistenceTestDatabase.CleanFoundationTablesAsync(context);
        }
    }
}