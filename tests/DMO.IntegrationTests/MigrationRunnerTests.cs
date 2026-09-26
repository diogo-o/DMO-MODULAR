using DMO.Application.Migrations;
using DMO.Infrastructure.Database;
using DMO.Infrastructure.Persistence;
using DMO.IntegrationTests.Host;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DMO.IntegrationTests;

/// <summary>
/// P1-T01/P1-T03 test â€” the migration mechanism works and Phase 1 models exactly the
/// persistence-foundation schema.
/// </summary>
/// <remarks>
/// PROPOSED â€” NOT EXECUTED. Awaiting Architect review before first execution.
/// See <c>docs/PROPOSED_TESTS_P1-T01.md</c> for the full test protocol record.
/// </remarks>
public sealed class MigrationRunnerTests
{
    [Fact]
    public void PersistenceContext_DeclaresExactlyTheFoundationEntities()
    {
        // Preconditions: the single application context, built without touching a database.
        var options = new DbContextOptionsBuilder<DmoDbContext>()
            .UseNpgsql(DmoWebApplicationFactory.PlaceholderConnectionString)
            .Options;

        using var context = new DmoDbContext(options);

        // P1-T03 assertion: the context models exactly the four persistence-foundation
        // tables (Migration 001 + 002). No other Phase 1 table is implied.
        //
        // P2-T04 (disclosed extension, contract Â§3 and Â§16.5): the accepted contract requires the
        // six domain-core tables to be mapped by the SAME single DbContext, discovered through
        // ApplyConfigurationsFromAssembly, so the exact modelled set necessarily grows by exactly
        // those six tables. P2-T05 (disclosed extension, P2-T05 contract Â§16/Â§25): the accepted
        // Controlo contract requires the eight Controlo tables to be mapped by the same single
        // DbContext, so the exact modelled set grows by exactly those eight tables. Post-closure
        // glass-density correction (contract Â§5.1): the one approved settings table is mapped by
        // the same single DbContext, growing the modelled set by exactly one table. P2-T06
        // (disclosed extension, P2-T06 contract Â§25): the one decision table is mapped by the same
        // single DbContext, growing the modelled set by exactly one table. P2-T07 (disclosed
        // extension, P2-T07 OWNER CLARIFICATION): the THREE Boquilhas register tables are mapped by
        // the same single DbContext, growing the modelled set by exactly those three tables (the
        // unreviewed 007 lifecycle tables are gone). Peso Comparação slice (disclosed extension,
        // ControloComparacaoDomain migration): the THREE Comparação tables (comparacoes,
        // comparacao_cm_subjects, comparacao_measurement_rows) are mapped by the same single
        // DbContext, growing the modelled set by exactly those three tables. The assertion is not
        // weakened â€” it still pins the complete modelled table set, and the forbidden-table
        // non-effect below is unchanged.
        var modelled = context.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            new[]
            {
                "admin_accounts", "boquilha_movement_audit", "boquilha_movements", "boquilhas",
                "bq_contexts", "cm_contexts", "comparacao_cm_subjects", "comparacao_measurement_rows", "comparacoes",
                "email_list_recipients", "email_lists",
                "email_templates", "glass_density_settings", "job_ons", "machine_repairer_assignments",
                "mf_contexts", "pdf_directory_settings", "peso_measurement_rows", "peso_review_decisions", "pesos", "repairers",
                "template_modules", "templates", "tool_machines", "tools", "users",
            },
            modelled);

        // Required non-effect: none of the forbidden Phase 1 tables is modelled.
        foreach (var forbidden in new[]
                 {
                     "permissions", "capabilities", "roles", "settings", "admin_audit_events",
                 })
        {
            Assert.DoesNotContain(forbidden, modelled);
        }

        // P2-T04 non-effect: no seventh domain-core table and no machine registry exist.
        foreach (var absent in new[]
                 {
                     "machines", "machine_registry", "tool_references", "documents",
                 })
        {
            Assert.DoesNotContain(absent, modelled);
        }
    }

    [Fact]
    public async Task ListPendingAsync_WithNoConfiguredConnection_Throws()
    {
        // Preconditions: infrastructure registered without a connection string.
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<DatabaseOptions>(_ => { });
        services.AddSingleton<DatabaseConnectionResolver>(provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;
            return new DatabaseConnectionResolver(options);
        });
        services.AddDbContext<DmoDbContext>((provider, builder) =>
        {
            var resolver = provider.GetRequiredService<DatabaseConnectionResolver>();
            builder.UseNpgsql(resolver.GetConnectionString());
        });
        services.AddScoped<IMigrationRunner, EfCoreMigrationRunner>();

        await using var provider = services.BuildServiceProvider();

        // Action + assertion: the runner refuses loudly instead of targeting a default database.
        //
        // The WHOLE attempted migration-path operation is inside the asserted delegate â€”
        // creating the scope, resolving IMigrationRunner and calling ListPendingAsync â€” because
        // database configuration is validated when the persistence services are resolved, not
        // only when the runner method executes. The accepted contract is "attempting the
        // migration path with no database configuration raises DatabaseConfigurationException";
        // it does not require the exception to originate specifically inside ListPendingAsync.
        var exception = await Assert.ThrowsAsync<DatabaseConfigurationException>(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
            await runner.ListPendingAsync();
        });

        // Required non-effect: the failure names the missing configuration rather than
        // silently targeting an implicit or default database.
        Assert.Contains(DatabaseOptions.ConnectionStringKey, exception.Message, StringComparison.Ordinal);
    }
}
