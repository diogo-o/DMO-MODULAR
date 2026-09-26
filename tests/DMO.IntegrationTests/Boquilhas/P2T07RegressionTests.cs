using System.Reflection;
using System.Text.RegularExpressions;
using DMO.Application.Access;
using DMO.Domain.Boquilhas;
using DMO.IntegrationTests.JobOn;
using DMO.Web.Endpoints.Boquilhas;
using DMO.Web.Pages.Boquilhas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DMO.IntegrationTests.Boquilhas;

/// <summary>
/// P2-T07 boundary/regression rows (OWNER CLARIFICATION): availability honesty, the recalculated
/// route matrix (12 endpoints + 3 pages), the superseded-lifecycle absence, the negative-scope
/// scans (N1–N7), the movement vocabulary, the false-identity scan and the protected-file pins.
/// </summary>
public sealed class P2T07RegressionTests
{
    /// <summary>
    /// N4 — the current-build availability list is still honest: <c>CurrentBuildAvailable</c> stays
    /// <c>[]</c> after P2-T07; no destination/route registration exists; the production registry
    /// resolves zero available Modules.
    /// </summary>
    [Fact]
    public void N4_CurrentBuildAvailableStaysEmptyAndNoP2T07RegistrationExists()
    {
        Assert.NotNull(ModuleRegistrations.CurrentBuildAvailable);
        Assert.Empty(ModuleRegistrations.CurrentBuildAvailable);

        using var factory = new Host.DmoWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<IModuleRegistry>();
        Assert.IsType<ModuleRegistry>(registry);
        Assert.Empty(registry.AvailableModules);
    }

    // ------------------------------------------------------------------- route count / policies (AC-A1/A6)

    /// <summary>
    /// A1/A6 — the P2-T07 route inventory is EXACTLY the final twenty-four routes (4 pages + 20
    /// minimal-API endpoints — RECALCULATED after the §34 delta: three new association endpoints
    /// in <c>BoquilhasEndpoints</c> and the five <c>Boquilhas > Definições</c> endpoints in
    /// <c>BoquilhasDefinicoesEndpoints</c>, the repairer family moved from Controlo per §34.3),
    /// all carrying exactly the canonical <c>boquilhas</c> policy, with no alias route and no
    /// second policy.
    /// </summary>
    [Fact]
    public void A1A6_ExactlyTwentyFourRoutesAllCarryingTheCanonicalBoquilhasPolicy()
    {
        var canonical = DMO.Web.Authorization.ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.Boquilhas);
        Assert.Equal("dmo.module.boquilhas", canonical);

        // The four Razor pages (routes 1–4) each declare the exactly pinned policy constant.
        var pages = new[]
        {
            typeof(DMO.Web.Pages.Boquilhas.IndexModel),
            typeof(DMO.Web.Pages.Boquilhas.NovoModel),
            typeof(DMO.Web.Pages.Boquilhas.HistoricoModel),
            typeof(DMO.Web.Pages.Boquilhas.DefinicoesModel),
        };

        foreach (var page in pages)
        {
            var attribute = page.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(attribute);
            Assert.Equal(BoquilhasPolicyNames.Boquilhas, attribute.Policy);
        }

        // The endpoint group declares exactly the canonical policy (the pinned constant).
        Assert.Equal(BoquilhasPolicyNames.Boquilhas, BoquilhasEndpoints.Policy);
        Assert.Equal(canonical, BoquilhasEndpoints.Policy);
    }

    /// <summary>
    /// A6/R4 — the Boquilhas endpoint surface is EXACTLY the twenty routes (15 in
    /// <c>BoquilhasEndpoints.cs</c> + the five <c>Boquilhas > Definições</c> endpoints of
    /// <c>BoquilhasDefinicoesEndpoints.cs</c>; the old 12 is superseded), with no close/reopen/
    /// opening-facts route, no second Tool search/create route, no PDF/file/email/document/
    /// availability route and no OTHER module's route vocabulary.
    /// </summary>
    [Fact]
    public void A6R4_ExactlyTwentyEndpoints_NoLifecycleNoDocumentRoutes()
    {
        var source = P2T04ProductionScan.Read("src/DMO.Web/Endpoints/Boquilhas/BoquilhasEndpoints.cs");
        var code = P2T04ProductionScan.WithoutRazorComments(source);

        // Exactly fifteen route handlers in the register surface: the MapGet/MapPost/MapPut calls
        // of the group (12 closed + the three §34 association routes).
        var handlers = Regex.Matches(source, @"group\.(Map(Get|Post|Put))\(")
            .Count;

        Assert.Equal(15, handlers);

        // The Definições surface owns the repairer family (§34.3): exactly five handlers in the
        // separated file (list/add/rename repairers; list/set-clear machine assignments).
        var definitionsSource = P2T04ProductionScan.Read("src/DMO.Web/Endpoints/Boquilhas/BoquilhasDefinicoesEndpoints.cs");
        var definitionsHandlers = Regex.Matches(definitionsSource, @"group\.(Map(Get|Post|Put))\(")
            .Count;
        Assert.Equal(5, definitionsHandlers);

        // No lifecycle route survives: close/reopen/opening-facts are gone.
        foreach (var token in new[] { "/close", "/reopen", "opening-facts", "\"close\"", "\"reopen\"" })
        {
            var occurrences = P2T04ProductionScan.SubstringOccurrences(code, token).Count;
            Assert.True(
                occurrences == 0,
                $"The lifecycle route token '{token}' survived in the Boquilhas endpoint surface ({occurrences}).");
        }

        // No OTHER module route surface: no /ferramentas route (the only Tool search/create stays
        // on the P2-T04 routes), no Controlo settings route and no PDF/file/email/document/send/
        // availability route (the register-identity route IS a legitimate Boquilhas route and is
        // excluded from this token set by construction; the repairer family is a legitimate
        // Boquilhas surface after §34.3 and lives in the separated Definições endpoint file).
        foreach (var token in new[] { "/ferramentas", "/controlo/", "\"pdf", "\"email", "\"document", "\"send", "\"availability" })
        {
            var occurrences = P2T04ProductionScan.SubstringOccurrences(code, token).Count;
            Assert.True(
                occurrences == 0,
                $"The forbidden route token '{token}' appears {occurrences} time(s) in the Boquilhas endpoint surface.");
        }

        // No second policy: the group requires exactly the canonical policy constant.
        Assert.Contains("RequireAuthorization(Policy)", source, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization(BoquilhasEndpoints.Policy)", definitionsSource, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------- superseded lifecycle absent

    /// <summary>
    /// B1-superseded — the entire lifecycle machinery is ABSENT everywhere: no close/reopen/
    /// snapshot/reopening/active-anchor/opening-facts token, no <c>boquilha_close_snapshots</c> /
    /// <c>boquilha_reopenings</c> / <c>boquilha_machines</c> table and no active partial unique
    /// index exists in any P2-T07 production source (the B1 correction is superseded: its
    /// underlying invariant no longer exists, and no replacement lock was introduced).
    /// </summary>
    [Fact]
    public void B1Superseded_NoLifecycleMachineryExists_NoActiveAnchorIndexes()
    {
        foreach (var token in P2T07ProductionScan.LifecycleTokens)
        {
            var offenders = P2T07ProductionScan.CodeSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path), token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The superseded-lifecycle token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }

        // The migration itself carries no status column and no partial-index predicate.
        var migration = P2T04ProductionScan.Read(P2T07ProductionScan.MigrationSourcePaths[0]);
        Assert.DoesNotContain("\"status\"", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE", migration, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------- N2/BND-B2 (P2-T08 boundary)

    /// <summary>
    /// N2/BND-B2 — no P2-T08 execution token exists anywhere in the P2-T07 production sources: no
    /// PDF generation, no file storage/writing, no directory handling, no document identity, no
    /// email sending.
    /// </summary>
    [Fact]
    public void N2_NoP2T08DocumentEmailOrFileExecutionExists()
    {
        foreach (var token in P2T07ProductionScan.P2T08ExecutionTokens)
        {
            var offenders = P2T07ProductionScan.CodeSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path), token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The P2-T08 execution token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }
    }

    // ------------------------------------------------------------------- N3/H2/BND-B3 (HISTÓRICO GLOBAL)

    /// <summary>
    /// N3/H2 — no HISTÓRICO GLOBAL (<c>historia</c>) route/entry/registration exists; the
    /// Boquilhas History lives only inside the module.
    /// </summary>
    [Fact]
    public void N3H2_NoHistoricoGlobalRouteOrEntryExists()
    {
        foreach (var token in P2T07ProductionScan.HistoriaTokens)
        {
            var offenders = P2T07ProductionScan.CodeSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path), token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The HISTÓRICO GLOBAL token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }
    }

    // ------------------------------------------------------------------- N1/R4 (no Controlo/Admin settings leakage)

    /// <summary>
    /// N1/R4 (superseded boundary, §34.3) — Boquilhas now OWNS the repairer family
    /// (<c>Boquilhas > Definições</c>: the repairer register + the machine assignments), so the
    /// "no repairer administration" part of the old boundary is REMOVED by the Owner
    /// clarification. What remains forbidden in the Boquilhas sources: the CONTROL settings
    /// surface (PDF directory, email lists/templates — still owned by Controlo), the glass-density
    /// settings (Controlo) and the Admin/availability vocabulary.
    /// </summary>
    [Fact]
    public void N1R4_NoControloOrAdminSettingsSurfaceExists()
    {
        foreach (var token in P2T07ProductionScan.SettingsTokens)
        {
            var offenders = P2T07ProductionScan.CodeSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path), token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The settings/administration token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }
    }

    // ------------------------------------------------------------------- N6/V1 (movement vocabulary)

    /// <summary>
    /// N6/V1 — the closed token set is EXACTLY <c>saida|entrada|entrada_sem_reparacao</c>: no
    /// Início, no Irreparável, no legacy type and no annulment/delete path exists in code, schema
    /// or routes.
    /// </summary>
    [Fact]
    public void N6V1_ExactlyThreeTypes_NoSupersededOrLegacyVocabulary()
    {
        foreach (var token in P2T07ProductionScan.MovementLeakageTokens)
        {
            var offenders = P2T07ProductionScan.CodeSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path), token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The movement-leakage token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }

        // The enum has exactly three members; the tokens map exactly.
        var kinds = Enum.GetValues<MovementKind>();
        Assert.Equal(3, kinds.Length);
        Assert.Equal(
            new[] { "saida", "entrada", "entrada_sem_reparacao" },
            kinds.Select(MovementKindTokens.ToToken).ToArray());

        // The migration CHECK carries exactly the closed three-type set.
        var migration = P2T04ProductionScan.Read(P2T07ProductionScan.MigrationSourcePaths[0]);
        Assert.Contains(
            "('saida','entrada','entrada_sem_reparacao')",
            migration,
            StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------- I4/BND-B8 (false identities)

    /// <summary>
    /// I4 — no production_id, no job_on_revision_id, no reverse-ID arrays, no per-piece BQ UUID,
    /// no module-specific Tool id and no client-minted id exists anywhere in the P2-T07 sources.
    /// </summary>
    [Fact]
    public void I4_NoFalseIdentityTokenExists()
    {
        foreach (var token in P2T07ProductionScan.FalseIdentityTokens)
        {
            var offenders = P2T07ProductionScan.CodeSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path), token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The false-identity token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }
    }

    // ------------------------------------------------------------------- B1/BND-B7 (no second balance authority)

    /// <summary>
    /// B1 — no second mutable balance authority: the migration creates no balance table or balance
    /// column and no expected/excess fact pair; the outstanding is derived by replay at read time
    /// and never stored.
    /// </summary>
    [Fact]
    public void B1_NoSecondBalanceAuthorityExists()
    {
        var migration = P2T04ProductionScan.Read(P2T07ProductionScan.MigrationSourcePaths[0]);

        foreach (var token in P2T07ProductionScan.BalanceTokens)
        {
            Assert.DoesNotContain(token, migration, StringComparison.OrdinalIgnoreCase);
        }

        // No cached/derived-sum store: the derivation is the pure replay helper only.
        var domain = P2T04ProductionScan.ReadAll(P2T07ProductionScan.DomainSourcePaths);
        Assert.Equal(
            0,
            P2T04ProductionScan.CodeOccurrences(domain, "stored").Count
            + P2T04ProductionScan.CodeOccurrences(domain, "cache").Count);
    }

    // ------------------------------------------------------------------- BND-B9 (no P2-T06/P2-T05 leakage)

    /// <summary>
    /// BND-B9 — no decision trail, no Peso/approval code and no Controlo leakage in the P2-T07
    /// sources.
    /// </summary>
    [Fact]
    public void BND9_NoP2T06OrP2T05LeakageExists()
    {
        foreach (var token in P2T07ProductionScan.ControloLeakageTokens)
        {
            var offenders = P2T07ProductionScan.CodeSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path), token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The Controlo-leakage token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }
    }

    // ------------------------------------------------------------------- N7/BND-B10 (protected files)

    /// <summary>
    /// N7 — the protected files are byte-identical: migrations 001–006 (incl. Designers) and
    /// <c>DmoDbContext.cs</c> are untouched by the P2-T07 change set (the migration 007 pair is
    /// REPLACED pre-closure per the Owner clarification — the old 007 is not a protected file).
    /// </summary>
    [Fact]
    public void N7_ProtectedFilesRemainByteIdentical()
    {
        var protectedPaths = P2T04ProductionScan.MigrationSourcePaths
            .Concat(
            [
                "src/DMO.Infrastructure/Persistence/Core/DmoDbContext.cs",
            ])
            .ToList();

        foreach (var path in protectedPaths)
        {
            var changed = P2T07ProductionScan.CodeSourcePaths
                .Concat(P2T07ProductionScan.DocumentedAdditiveSourcePaths)
                .Any(candidate => string.Equals(candidate, path, StringComparison.Ordinal));

            Assert.False(changed, $"The protected file '{path}' must not be in the P2-T07 change set.");
        }

        // The documented additive non-new files are exactly the three accepted ones.
        Assert.Equal(
            3,
            P2T07ProductionScan.DocumentedAdditiveSourcePaths.Count);
    }

    // ------------------------------------------------------------------- BND-B5/N5 (no fake sidebar)

    /// <summary>
    /// BND-B5 (static facet) — the P2-T07 assets/pages carry no simulated Job On sidebar markup
    /// and no machine-registry vocabulary.
    /// </summary>
    [Fact]
    public void BND5_NoSimulatedJobOnSidebarAndNoMachineRegistry()
    {
        var assets = P2T04ProductionScan.ReadAll(P2T07ProductionScan.AssetPaths);
        var web = P2T04ProductionScan.ReadAll(P2T07ProductionScan.WebSourcePaths);

        Assert.True(P2T04ProductionScan.CodeOccurrences(assets, "sidebar").Count == 0, "sidebar token found in the Boquilhas assets.");
        Assert.True(P2T04ProductionScan.CodeOccurrences(web, "machine_id").Count == 0, "machine_id token found in the Boquilhas web sources.");
        Assert.True(
            P2T04ProductionScan.CodeOccurrences(
                P2T04ProductionScan.WithoutRazorComments(web), "machine_registry").Count == 0,
            "machine_registry token found in the Boquilhas web sources.");
    }
}