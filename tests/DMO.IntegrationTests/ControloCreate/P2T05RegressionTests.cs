using System.Net;
using System.Reflection;
using DMO.Application.Access;
using DMO.Application.ControloCreate;
using DMO.Domain.Controlo;
using DMO.Infrastructure.Persistence.Entities;
using DMO.IntegrationTests.JobOn;
using DMO.Web.Authorization;
using DMO.Web.Endpoints;
using DMO.Web.Pages.Controlo;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DMO.IntegrationTests.ControloCreate;

/// <summary>
/// P2-T05 integration — the boundary/regression block: interim runtime state, migration identity,
/// protected-file byte-identity, excluded vocabulary, changed-path allow-list and fixed-desktop
/// static rules.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §21.6 (interim runtime state), §25 (migration contract), §26.4 rows
/// PID5/MAC6/REP4/SNA5/BND1–BND9/LAY1 and §30 AC-P1/AC-P3/AC-D4/AC-E4/AC-H3/AC-Y1–AC-Y7/AC-N1.
/// </remarks>
public sealed class P2T05RegressionTests
{
    /// <summary>
    /// The protected persistence foundation pinned by normalized content hash (contract §25.3 and
    /// AC-Y6): <c>DmoDbContext.cs</c> and migrations 001/002/003 (incl. Designers) are never
    /// edited.
    /// </summary>
    private static readonly Dictionary<string, string> PinnedProtectedFoundation = new(StringComparer.Ordinal)
    {
        ["src/DMO.Infrastructure/Persistence/DmoDbContext.cs"] = "32a1b78f1c393f3e19f27ebf0012c78c9ad96320de2afb4d1f33ae6aa844f2c5",
        ["src/DMO.Infrastructure/Migrations/20260922001736_AccountAndTemplateFoundation.cs"] = "4f5a8136547158fa6fe1b6a404d750e0a113a10c083359bdd1c0e901524c354f",
        ["src/DMO.Infrastructure/Migrations/20260922001736_AccountAndTemplateFoundation.Designer.cs"] = "6a68107f84beb8d893117eef38280ee0a33dd7fe3d6106208766f3c391c46f7a",
        ["src/DMO.Infrastructure/Migrations/20260922001757_TemplateModuleComposition.cs"] = "f9e17cc8fbd99077cc7c570cc80f9c16ea1d9acb7dbd6b3d7caaa3631f34b7fe",
        ["src/DMO.Infrastructure/Migrations/20260922001757_TemplateModuleComposition.Designer.cs"] = "0fba2f103fe768d32da82d4301ad02e5095cde2d18706424feb06ead19e85dc8",
        ["src/DMO.Infrastructure/Migrations/20260922232349_ToolJobOnDomainCore.cs"] = "4fb6a2d9a110830e26a100a4b1eb33a928cfee01d28b7c600e223b59dc6fef8d",
        ["src/DMO.Infrastructure/Migrations/20260922232349_ToolJobOnDomainCore.Designer.cs"] = "9a6cd0b2fd52683d357bef0ab7b12132d9892f4438625d6b43a6985b6cff79cc",
    };

    /// <summary>
    /// BND1 — the current-build availability list is still honest: <c>CurrentBuildAvailable</c>
    /// stays <c>[]</c> after P2-T05; the production registry resolves zero available Modules; the
    /// canonical vocabulary is still the full 13 identities (AC-Y1, §21.6).
    /// </summary>
    [Fact]
    public void BND1_CurrentBuildAvailableStaysEmptyAndDefinicoesIsNotADestination()
    {
        Assert.NotNull(ModuleRegistrations.CurrentBuildAvailable);
        Assert.Empty(ModuleRegistrations.CurrentBuildAvailable);

        using var factory = new Host.DmoWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<IModuleRegistry>();

        Assert.IsType<ModuleRegistry>(registry);
        Assert.Empty(registry.AvailableModules);
        Assert.Equal(13, registry.KnownModuleIds.Count);

        // Definições is not registered anywhere: no Module identity and no destination id.
        Assert.DoesNotContain(
            ModuleCatalog.All,
            entry => entry.Id.Value.Contains("defini", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            ModuleCatalog.All,
            entry => entry.DestinationId?.Contains("defini", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// PID5 (S) — no legacy/fake production identity token and no legacy status appears in any
    /// P2-T05 source in code/markup/schema: <c>production_id</c>, <c>job_on_revision_id</c>,
    /// <c>revisionId</c> and <c>rascunho</c> occur nowhere outside comments (AC-P1/AC-P3).
    /// </summary>
    [Fact]
    public void PID5_NoLegacyOrFakeProductionIdentityTokenAppearsInAnyP2T05Source()
    {
        Assert.NotEmpty(P2T05ProductionScan.ProductionSourcePaths);

        foreach (var token in P2T05ProductionScan.LegacyIdentityTokens)
        {
            var offenders = P2T05ProductionScan.ProductionSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The legacy identity token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }

        // Schema-level proof: the eight created tables carry no legacy identity column.
        var migration = P2T04ProductionScan.Read(P2T05ProductionScan.MigrationSourcePaths[0]);

        Assert.DoesNotContain("production_id", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("revision", migration, StringComparison.Ordinal);
    }

    /// <summary>
    /// BND2 (S) — no P2-T06 leakage: no approve/reject/reopen member, route, type, table or
    /// presentation exists in any P2-T05 source; the approved/rejected status VALUES are the only
    /// sanctioned approval vocabulary (status CHECK, §16.1).
    /// </summary>
    [Fact]
    public void BND2_NoP2T06ApprovalLeakageExists()
    {
        foreach (var token in P2T05ProductionScan.ApprovalLeakageTokens)
        {
            var offenders = P2T05ProductionScan.ProductionSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The approval-leakage token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }

        // No approval table exists in the migration.
        var created = P2T05ProductionScan.CreatedTableNames();
        Assert.Equal(8, created.Count);
        Assert.DoesNotContain(created, table =>
            table.Contains("approval", StringComparison.OrdinalIgnoreCase)
            || table.Contains("decision", StringComparison.OrdinalIgnoreCase)
            || table.Contains("review", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// BND3 (S) — no P2-T07 leakage: no Boquilhas aggregate/movement type, no repairer-resolution
    /// machinery and no assignment-history table exists.
    /// </summary>
    [Fact]
    public void BND3_NoP2T07BoquilhasLeakageExists()
    {
        foreach (var token in P2T05ProductionScan.BoquilhasLeakageTokens)
        {
            var offenders = P2T05ProductionScan.ProductionSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The Boquilhas-leakage token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }

        Assert.DoesNotContain(P2T05ProductionScan.CreatedTableNames(), table =>
            table.Contains("assignment_history", StringComparison.OrdinalIgnoreCase)
            || table.Contains("movement", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// BND4 (S) — no P2-T08 leakage: no PDF/file-generation, email-sending, availability-read,
    /// routing-rule or placeholder-parsing code exists. The directory probe's own probe-file IO is
    /// the contracted exception (§12.2) and is NOT scanned.
    /// </summary>
    [Fact]
    public void BND4_NoP2T08DocumentGenerationOrSendingLeakageExists()
    {
        foreach (var token in P2T05ProductionScan.DocumentLeakageTokens)
        {
            var offenders = P2T05ProductionScan.ProductionSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The document-leakage token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }
    }

    /// <summary>
    /// BND5 (S) — no duplicate Tool/JobOn/CM identity: P2-T05 creates no second registry; the only
    /// P2-T04 persistence types referenced from P2-T05 production code are the contracted ones:
    /// the read-only <c>CmContextEntity</c> of the sanctioned anchor-traversal read
    /// (DmoPesoContextRead), the FK declaration in the Peso configuration and the dependency-probe
    /// lookup (the contracted §20.4.1 pending-same-Tool case).
    /// </summary>
    [Fact]
    public void BND5_NoDuplicateToolJobOnOrCmIdentityExists()
    {
        var cmEntitySources = P2T05ProductionScan.ProductionSourcePaths
            .Where(path => P2T04ProductionScan.CodeOccurrences(
                P2T04ProductionScan.Read(path),
                string.Concat("Cm", "Context", "Entity")).Count > 0)
            .ToList();

        Assert.Equal(
            new[]
            {
                "src/DMO.Infrastructure/Persistence/DmoPesoContextRead.cs",
                "src/DMO.Infrastructure/Persistence/EntityConfigurations/PesoEntityConfiguration.cs",
                "src/DMO.Infrastructure/Persistence/PesoJobOnDependencyProbe.cs",
                // EF-generated: the migration Designers declare the FK to cm_contexts by type name.
                "src/DMO.Infrastructure/Migrations/20260923045054_ControloCreateDomain.Designer.cs",
                // EF-generated: the glass-density correction migration's Designer (the newest
                // designer) declares the complete model, including the cm_contexts FK by type name.
                "src/DMO.Infrastructure/Migrations/20260923122429_GlassDensitySettings.Designer.cs",
            }.OrderBy(path => path, StringComparer.Ordinal),
            cmEntitySources.OrderBy(path => path, StringComparer.Ordinal));

        foreach (var token in new[]
                 {
                     string.Concat("Tool", "Repository"),
                     string.Concat("Job", "On", "Repository"),
                     string.Concat("I", "Tool", "Repository"),
                     string.Concat("I", "Job", "On", "Repository"),
                 })
        {
            var offenders = P2T05ProductionScan.ProductionSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The duplicate-registry token '{token}' appears in: {string.Join(", ", offenders)}.");
        }
    }

    /// <summary>
    /// BND6 (S) + MAC6 (U/S) — no machine registry, no <c>machine_id</c>, no assignment history and
    /// no line/group concept exists in the P2-T05 schema or code (AC-E4).
    /// </summary>
    [Fact]
    public void BND6_NoMachineRegistryOrLineGroupingExists()
    {
        foreach (var token in P2T05ProductionScan.QuotedSchemaTokens)
        {
            var offenders = P2T05ProductionScan.ProductionSourcePaths
                .Where(path => P2T04ProductionScan.Read(path)
                    .Contains(token, StringComparison.Ordinal))
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The excluded schema name {token} is declared in: {string.Join(", ", offenders)}.");
        }

        foreach (var token in P2T05ProductionScan.LineGroupingTokens)
        {
            var offenders = P2T05ProductionScan.ProductionSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The line-grouping token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }

        // Reflection proof: the domain assignment record carries no group/line member.
        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var assignmentMembers = typeof(MachineRepairerAssignment).GetProperties(flags);

        Assert.DoesNotContain(assignmentMembers, member =>
            member.Name.Contains("Group", StringComparison.Ordinal)
            || member.Name.Contains("Line", StringComparison.Ordinal)
            || member.Name.Contains("Linha", StringComparison.Ordinal));
    }

    /// <summary>
    /// BND7 (S) — no Comparação/Pegamentos/Folha/Resumo table, column, type or route exists in the
    /// P2-T05 sources (Q-SCOPE seam, AC-Y5). The legitimate email-template <c>document_type</c>
    /// CHECK values are contracted and are not scanned as identity forms.
    /// <para>
    /// <b>Owned display-surface disclosure</b>: the ONE P2-T05 owned page that legitimately renders
    /// the accepted plural title <c>Resumos desta referência</c> (the Resumo landing page's
    /// production-switcher heading) is excluded from this row's identity-token scan — the title is
    /// user-visible text, not an identity (<see cref="P2T05ProductionScan.DisclosedResumoSurfaceDisplayPath"/>).
    /// The exclusion is asserted to be never vacuous: the file exists and really carries the
    /// accepted display title.
    /// </para>
    /// </summary>
    [Fact]
    public void BND7_NoComparacaoPegamentosFolhaResumoIdentityExists()
    {
        var disclosed = new[] { P2T05ProductionScan.DisclosedResumoSurfaceDisplayPath };

        foreach (var token in P2T05ProductionScan.ScopeBoundaryTokens)
        {
            var offenders = P2T05ProductionScan.ProductionSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .Where(path => !disclosed.Contains(path, StringComparer.Ordinal))
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The scope-boundary token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }

        // The owned display-surface disclosure is real, not vacuous: the disclosed file exists and
        // really carries the accepted plural display title — no other path is excused from the
        // identity-token scan.
        foreach (var path in disclosed)
        {
            Assert.True(P2T04ProductionScan.Exists(path), $"Disclosed display path '{path}' is missing.");
            Assert.Contains(
                "Resumos desta referência",
                P2T04ProductionScan.Read(path),
                StringComparison.Ordinal);
        }

        Assert.DoesNotContain(P2T05ProductionScan.CreatedTableNames(), table =>
            table.Contains("compara", StringComparison.OrdinalIgnoreCase)
            || table.Contains("pegamento", StringComparison.OrdinalIgnoreCase)
            || table.Contains("resumo", StringComparison.OrdinalIgnoreCase)
            || table.Contains("sheet", StringComparison.OrdinalIgnoreCase));

        // No Peso column is a previous-Peso relation: the pesos entity has no such member.
        Assert.DoesNotContain(
            typeof(PesoEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.Name.Contains("PreviousPeso", StringComparison.Ordinal));
    }

    /// <summary>
    /// BND8 — migrations 001/002/003 and <c>DmoDbContext.cs</c> are byte-identical to the P2-T04
    /// pinned baselines, and the frozen shared-frontend artifacts remain byte-identical (AC-Y6).
    /// </summary>
    [Fact]
    public void BND8_Migration001002003AndDmoDbContextAreByteIdentical()
    {
        foreach (var (relativePath, expectedHash) in PinnedProtectedFoundation)
        {
            Assert.True(
                P2T04ProductionScan.Exists(relativePath),
                $"Protected persistence-foundation file '{relativePath}' is missing.");

            Assert.Equal(expectedHash, P2T04ProductionScan.HashFile(relativePath));
        }

        // The accepted P2-T04 pins of the shared-frontend artifacts, read by reflection (the
        // pinned dictionaries live in the accepted P2-T04 regression suite).
        var sharedField = typeof(P2T04RegressionTests).GetField(
            "PinnedSharedFrontendArtifacts",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (sharedField is not null)
        {
            var pinned = (Dictionary<string, string>)sharedField.GetValue(null)!;

            foreach (var (relativePath, expectedHash) in pinned)
            {
                Assert.True(
                    P2T04ProductionScan.Exists(relativePath),
                    $"Frozen shared-frontend artifact '{relativePath}' is missing.");

                Assert.Equal(expectedHash, P2T04ProductionScan.HashFile(relativePath));
            }
        }

        // dmo-components.css carries no P2-T05 marker (nothing was appended to the frozen
        // component stylesheet — §23.3 rule 1).
        var componentsCss = P2T04ProductionScan.Read("src/DMO.Web/wwwroot/css/dmo-components.css");

        Assert.DoesNotContain("dmo-controlo", componentsCss, StringComparison.Ordinal);
    }

    /// <summary>
    /// BND9 (S) — the changed-path allow-list holds: every <c>src</c> file that mentions the
    /// P2-T05 vocabulary is inside the P2-T05 owned paths or is one of the documented additive
    /// files (Program.cs, PersistenceServiceCollectionExtensions.cs, the EF snapshot and the three
    /// sanctioned Q-CAND Job On application files) (AC-Y7).
    /// <para>
    /// <b>Outputs slice disclosed extension</b> (Peso outputs on the Job On sheet — this slice):
    /// the Job On owned files of the outputs slice NECESSARILY carry the Peso vocabulary of what
    /// they expose — the <c>jobon_id → peso_id</c> read projection and service, the controlled
    /// <c>peso-pdf</c> open route and the sheet outputs section. They are disclosed as an accepted
    /// extension of the P2-T05 owned surface (P2-T05 §31.1: the Peso reaches Controlo through the
    /// Job On occurrence; <see cref="P2T05ProductionScan.DisclosedOutputsSliceSourcePaths"/>), and
    /// each disclosed file is asserted below to REALLY carry the additive outputs vocabulary
    /// (never vacuous) — the same files are disclosed for the P2-T04-side scan by the P2-T04 BND7
    /// row. No other file gains the P2-T05 vocabulary.
    /// </para>
    /// </summary>
    [Fact]
    public void BND9_TheChangedPathAllowListHoldsForEveryP2T05VocabularyMention()
    {
        var offenders = P2T05ProductionScan.VocabularyMentionsOutsideOwnedPaths();

        Assert.True(
            offenders.Count == 0,
            $"Files outside the P2-T05 owned paths mention the P2-T05 vocabulary: {string.Join(", ", offenders)}.");

        // The allow-list is real, not vacuous: the vocabulary is present inside the owned paths and
        // in the documented additive composition files.
        foreach (var expected in new[]
                 {
                     "src/DMO.Web/Program.cs",
                     "src/DMO.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs",
                     "src/DMO.Infrastructure/Migrations/DmoDbContextModelSnapshot.cs",
                     "src/DMO.Application/JobOn/IJobOnService.cs",
                     "src/DMO.Web/Endpoints/ControloCreateEndpoints.cs",
                     "src/DMO.Web/Pages/Controlo/Create.cshtml",
                 })
        {
            Assert.True(
                P2T04ProductionScan.Exists(expected),
                $"Expected P2-T05 path '{expected}' is missing.");
        }

        // The outputs-slice exclusion is real, not vacuous: every disclosed Job On outputs file
        // exists and actually carries the additive Peso output vocabulary (the read projection /
        // service contract / implementation / controlled peso-pdf open route / sheet section).
        foreach (var path in P2T05ProductionScan.DisclosedOutputsSliceSourcePaths)
        {
            Assert.True(
                P2T04ProductionScan.Exists(path),
                $"Disclosed outputs-slice path '{path}' is missing.");

            var source = P2T04ProductionScan.Read(path);

            Assert.True(
                source.Contains(string.Concat("Pe", "so"), StringComparison.Ordinal),
                $"Disclosed outputs-slice path '{path}' does not carry the additive Peso output vocabulary.");
        }
    }

    /// <summary>
    /// REP4 (S) — no invented repairer field exists: the repairer domain record and entity expose
    /// only the contracted members, and no repairer-scoped field token appears in any P2-T05
    /// source (AC-D4).
    /// </summary>
    [Fact]
    public void REP4_NoInventedRepairerFieldExists()
    {
        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        foreach (var type in new[] { typeof(Repairer), typeof(RepairerEntity) })
        {
            foreach (var member in type.GetProperties(flags))
            {
                Assert.DoesNotContain(
                    P2T05ProductionScan.RepairerFieldTokens,
                    token => member.Name.Contains(token, StringComparison.Ordinal));
            }
        }

        // The repairer register is name-only in the schema too: the repairers CreateTable block (not the
        // whole migration — email_list_recipients legitimately declares an "address" column) carries
        // no invented business column.
        var migration = P2T04ProductionScan.Read(P2T05ProductionScan.MigrationSourcePaths[0]);
        var repairersBlock = System.Text.RegularExpressions.Regex.Match(
            migration,
            @"CreateTable\(\s*name: ""repairers""(?<block>.*?)(?=CreateTable\(\s*name:|\z)",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        Assert.True(repairersBlock.Success, "The migration must declare the repairers table.");

        var repairers = repairersBlock.Groups["block"].Value;

        // The EF-generated column declarations use `column = table.Column<…>` (no quotes).
        Assert.DoesNotContain("phone = table.Column", repairers, StringComparison.Ordinal);
        Assert.DoesNotContain("nif = table.Column", repairers, StringComparison.Ordinal);
        Assert.DoesNotContain("supplier", repairers, StringComparison.Ordinal);
        Assert.DoesNotContain("contact", repairers, StringComparison.Ordinal);
        Assert.DoesNotContain("address = table.Column", repairers, StringComparison.Ordinal);
        Assert.DoesNotContain("email = table.Column", repairers, StringComparison.Ordinal);
        Assert.Contains("name = table.Column", repairers, StringComparison.Ordinal);
    }

    /// <summary>
    /// SNA5 (S) — no code path copies live Tool values into Peso rows and no snapshot engine or
    /// snapshot table exists (AC-H3).
    /// </summary>
    [Fact]
    public void SNA5_NoLiveToolCopyAndNoSnapshotEngineExists()
    {
        foreach (var token in P2T05ProductionScan.SnapshotEngineTokens)
        {
            var offenders = P2T05ProductionScan.ProductionSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The snapshot-engine token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }

        // The Peso row has NO Tool-value copy: the entity exposes the anchor ids and the Peso-owned
        // facts only.
        var pesoColumns = typeof(PesoEntity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain(pesoColumns, column =>
            column.Contains("ToolReference", StringComparison.Ordinal)
            || column.Contains("ToolLot", StringComparison.Ordinal)
            || column.Contains("ToolType", StringComparison.Ordinal));
    }

    /// <summary>
    /// AUT5 (S) — no P2-T05 route carries a second policy: the endpoint sources declare only the
    /// canonical <c>controlo-create</c> policy, the page <c>[Authorize]</c> constant is asserted
    /// equal to the canonical projection, and no route carries <c>controlo-approve</c> or
    /// <c>dmo.administration</c> (AC-G1).
    /// </summary>
    [Fact]
    public void AUT5_EveryP2T05RouteCarriesExactlyTheCanonicalControloCreatePolicy()
    {
        Assert.True(ControloPolicyNames.MatchesCanonicalPolicy());

        // The pinned static property used by the endpoints group is the canonical projection; the
        // Definições group declares the same canonical generator call inline.
        Assert.Equal(
            ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.ControloCreate),
            ControloCreateEndpoints.Policy);

        Assert.Contains(
            "ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.ControloCreate)",
            P2T04ProductionScan.Read("src/DMO.Web/Endpoints/ControloDefinicoesEndpoints.cs"),
            StringComparison.Ordinal);

        foreach (var path in new[]
                 {
                     "src/DMO.Web/Endpoints/ControloCreateEndpoints.cs",
                     "src/DMO.Web/Endpoints/ControloDefinicoesEndpoints.cs",
                 })
        {
            var source = P2T04ProductionScan.Read(path);

            // RequireAuthorization calls are the only policy declarations; each one references the
            // canonical ControloCreate policy surface (never a literal second policy).
            var policies = System.Text.RegularExpressions.Regex.Matches(
                    source,
                    @"RequireAuthorization\((?<policy>[^)]+)\)")
                .Select(match => match.Groups["policy"].Value)
                .ToList();

            Assert.NotEmpty(policies);

            Assert.All(policies, policy =>
            {
                // Both endpoint files reference the canonical policy surface: the endpoints file
                // uses the pinned static property, the Definições file the generator projection.
                Assert.True(
                    policy.Contains("ControloCreate", StringComparison.Ordinal)
                    || policy == "Policy",
                    $"Unexpected policy declaration '{policy}'.");
                Assert.DoesNotContain("Approve", policy, StringComparison.Ordinal);
                Assert.DoesNotContain("administration", policy, StringComparison.Ordinal);
            });

            Assert.Empty(P2T04ProductionScan.CodeOccurrences(source, "dmo.module.controlo-approve"));
            Assert.Empty(P2T04ProductionScan.CodeOccurrences(source, "dmo.administration"));
        }

        var pages = new[]
        {
            "src/DMO.Web/Pages/Controlo/Create.cshtml.cs",
            "src/DMO.Web/Pages/Controlo/Definicoes.cshtml.cs",
        };

        foreach (var page in pages)
        {
            Assert.Contains(
                "Authorize(Policy = ControloPolicyNames.ControloCreate)",
                P2T04ProductionScan.Read(page),
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// LAY1 (S) — the P2-T05-owned surface is breakpoint-free: no <c>@media</c>/<c>@container</c>/
    /// <c>@supports</c> structural rule, no width listener, no table→card conversion, no
    /// required-column hiding and no action relocation in the stylesheet, the script or the page
    /// markup (AC-N1).
    /// </summary>
    [Fact]
    public void LAY1_NoBreakpointRuleCardConversionOrWidthListenerExists()
    {
        var css = P2T04ProductionScan.Read("src/DMO.Web/wwwroot/css/dmo-controlo.css");
        var rules = P2T04ProductionScan.WithoutCssComments(css);

        Assert.False(string.IsNullOrWhiteSpace(rules), "The P2-T05 stylesheet must carry real rules.");

        foreach (var breakpoint in new[] { "@media", "@container", "@supports" })
        {
            Assert.DoesNotContain(breakpoint, rules, StringComparison.Ordinal);
        }

        foreach (var widthCondition in new[]
                 {
                     "matchMedia", "ResizeObserver", "addEventListener('resize'", "innerWidth",
                     "clientWidth", "offsetWidth",
                 })
        {
            Assert.DoesNotContain(widthCondition, rules, StringComparison.Ordinal);
        }

        var script = P2T04ProductionScan.Read("src/DMO.Web/wwwroot/js/dmo-controlo.js");

        Assert.Contains("if (!window.dmoControlo)", script, StringComparison.Ordinal);

        foreach (var widthCondition in new[]
                 {
                     "matchMedia", "ResizeObserver", "addEventListener('resize'", "innerWidth",
                 })
        {
            Assert.DoesNotContain(widthCondition, script, StringComparison.Ordinal);
        }

        // No table→card conversion marker and no hiding of required columns in the markup: the
        // tables are present with their full heading sets in the page sources (the §34.3 delta
        // moved the repairers/assignments tables to Boquilhas > Definições).
        var createMarkup = P2T04ProductionScan.Read("src/DMO.Web/Pages/Controlo/Create.cshtml");
        var definicoesMarkup = P2T04ProductionScan.Read("src/DMO.Web/Pages/Controlo/Definicoes.cshtml");

        Assert.Contains("data-dmo-results-table", createMarkup, StringComparison.Ordinal);
        Assert.Contains("dmo-controlo__settings-table", definicoesMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-dmo-assignments-table", definicoesMarkup, StringComparison.Ordinal);

        // The moved family is really rendered by the Boquilhas surface.
        var boquilhasMarkup = P2T04ProductionScan.Read("src/DMO.Web/Pages/Boquilhas/Definicoes.cshtml");
        Assert.Contains("data-dmo-assignments-table", boquilhasMarkup, StringComparison.Ordinal);
        Assert.Contains("data-dmo-repairers-table", boquilhasMarkup, StringComparison.Ordinal);
    }
}