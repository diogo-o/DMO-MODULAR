using System.Reflection;
using DMO.Application.Access;
using DMO.Application.Controlo.Approve;
using DMO.Application.Controlo.Pesos;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;
using DMO.Infrastructure.Persistence.Entities;
using DMO.IntegrationTests.JobOn;
using DMO.Web.Authorization;
using DMO.Web.Endpoints.Controlo;
using DMO.Web.Pages.Controlo.Approve;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DMO.IntegrationTests.Controlo.Approve;

/// <summary>
/// P2-T06 integration â€” the boundary/regression block (contract Â§26.4 rows BND1â€“BND8, I4/I5,
/// D2/D3/D6, CP1/CP2, R2, O5, H4, A5, L1/L3 and App. A): interim runtime state, protected-file
/// byte identity, excluded vocabularies, the exact route count/policies, mechanical scans of the
/// write-path restriction and the fixed-desktop static rules.
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract Â§13.3/Â§14 (interim runtime + policies), Â§19.5/Â§22 (negative-scope
/// protections), Â§25 (migration contract) and App. A (protected boundaries).</remarks>
public sealed class P2T06RegressionTests
{
    // ------------------------------------------------------------------- BND1 (AC-B4)

    /// <summary>
    /// BND1 â€” the current-build availability list is still honest: <c>CurrentBuildAvailable</c>
    /// stays <c>[]</c> after P2-T06; the production registry resolves zero available Modules; no
    /// P2-T06 destination is registered anywhere (contract Â§13.3).
    /// </summary>
    [Fact]
    public void BND1_CurrentBuildAvailableStaysEmptyAndNoP2T06RegistrationExists()
    {
        Assert.NotNull(ModuleRegistrations.CurrentBuildAvailable);
        Assert.Empty(ModuleRegistrations.CurrentBuildAvailable);

        using var factory = new Host.DmoWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<IModuleRegistry>();
        Assert.IsType<ModuleRegistry>(registry);
        Assert.Empty(registry.AvailableModules);
    }

    // ------------------------------------------------------------------- BND2 (AC-B2) / P2-T08 boundary

    /// <summary>
    /// BND2 â€” updated by the P2-T08 documents slice: the Peso PDF generation surface lives on
    /// Controlo_CREATE (Create owns the operational document work after the decision â€” the
    /// sanctioned documents surface: the <c>src/DMO.Application/Documents</c> area,
    /// <c>DocumentsEndpoints.cs</c>, the composition-root wiring in <c>Program.cs</c> and the
    /// page-owned action hooks of the Create surface/adapters), while the Controlo APPROVE area
    /// stays free of Peso PDF vocabulary: Approve owns only the decision. No email
    /// sending/routing, no attachments, no document identity, no regeneration, no raw file-stream
    /// mechanics and no Pegamentos/Resume execution exist anywhere in the P2-T06 area (contract
    /// Â§23/Â§22, BND-B2).
    /// </summary>
    [Fact]
    public void BND2_PesoPdfExecutionIsSanctionedOnCreateAndNoEmailOrOtherDocumentExecutionExists()
    {
        // Still-forbidden P2-T08 mechanics: zero occurrences everywhere in the P2-T06 area.
        foreach (var token in P2T06ProductionScan.P2T08ForbiddenExecutionTokens)
        {
            var offenders = P2T06ProductionScan.ProductionSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The P2-T08 execution token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }

        // The Peso PDF vocabulary NEVER appears in the P2-T06 area (the Approve surface owns no
        // document work): the only allowed mention is the composition-root wiring of the shared
        // documents service in Program.cs.
        var sanctionedPdfSources = new[]
        {
            "src/DMO.Web/Program.cs",
        };

        var nonSanctionedSources = P2T06ProductionScan.ProductionSourcePaths
            .Where(path => !sanctionedPdfSources.Contains(path, StringComparer.Ordinal))
            .ToList();

        foreach (var token in P2T06ProductionScan.PesoPdfExecutionTokens)
        {
            var offenders = nonSanctionedSources
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The Peso PDF token '{token}' appears outside the sanctioned composition wiring in: {string.Join(", ", offenders)}.");
        }

        // No send route exists in the endpoint surface (route-count statement Â§13.2).
        Assert.DoesNotContain("send", typeof(ControloApproveEndpoints).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name), StringComparer.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------- BND3 (AC-B3)

    /// <summary>BND3 â€” no Boquilhas behavior exists in any P2-T06 source.</summary>
    [Fact]
    public void BND3_NoP2T07BoquilhasLeakageExists()
    {
        foreach (var token in P2T06ProductionScan.BoquilhasTokens)
        {
            var offenders = P2T06ProductionScan.ProductionSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The Boquilhas-leakage token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }
    }

    // ------------------------------------------------------------------- BND1 (AC-B1) / settings exclusion

    /// <summary>
    /// BND1 â€” no Definições/operational-setting surface exists under Approve: no repairer/
    /// machine-assignment/PDF-directory/email-list/email-template/glass-density code, route or
    /// type in any P2-T06 source (contract Â§2.2, BND-B1).
    /// </summary>
    [Fact]
    public void BND1_NoDefinicoesOrOperationalSettingSurfaceExistsInApprove()
    {
        var offenders = P2T06ProductionScan.ProductionSourcePaths
            .Where(path => P2T06ProductionScan.SettingsTokens.Any(token =>
                P2T04ProductionScan.CodeOccurrences(P2T04ProductionScan.Read(path), token).Count > 0))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"A settings/Definições token appears in P2-T06 sources: {string.Join(", ", offenders)}.");

        // No settings route/type in the surface assembly either.
        Assert.DoesNotContain(
            typeof(ControloApproveEndpoints).Assembly.GetTypes(),
            type => type.Namespace?.Contains("ControloApprove", StringComparison.Ordinal) == true
                && (type.Name.Contains("Definicoes", StringComparison.Ordinal)
                    || type.Name.Contains("Repairer", StringComparison.Ordinal)
                    || type.Name.Contains("Email", StringComparison.Ordinal)
                    || type.Name.Contains("Density", StringComparison.Ordinal)
                    || type.Name.Contains("Pdf", StringComparison.Ordinal)));
    }

    // ------------------------------------------------------------------- BND4 (AC-B4) covered by BND1 availability; H4 (AC-H4)

    /// <summary>H4 (AC-H4) â€” no HISTÃ“RICO GLOBAL (<c>historia</c>) route/entry/registration exists;
    /// the local Histórico lives only inside the Approve surface. BND5 covers the same scan.</summary>
    [Fact]
    public void H4_NoHistoricoGlobalRouteOrEntryExists()
    {
        foreach (var token in P2T06ProductionScan.HistoriaTokens)
        {
            var offenders = P2T06ProductionScan.ProductionSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The HISTÃ“RICO GLOBAL token '{token}' appears in P2-T06 sources: {string.Join(", ", offenders)}.");
        }
    }

    // ------------------------------------------------------------------- BND6 (AC-B6) / deferred carriers (D6/CP2)

    /// <summary>
    /// BND6/D6/CP2 — no deferred-carrier invention: no <c>previous_peso_id</c>, no per-CM
    /// decision table/route/type, no <c>controlo_sheet_id</c>/Folha table/column/route, no
    /// send table — nothing is fabricated (contract §17.3/§18.3, BND-B6; Q-PERCM/Q-FOLHA/Q-COMP).
    /// The single CP4 composition seam (contract §26.4 row CP4, <c>ComparisonComposer.cs</c>) is
    /// the one contracted file allowed to carry the closed relation vocabulary in code — it IS
    /// the contract pin for the future carrier, not an invented carrier: every other P2-T06
    /// source stays free of the tokens, and the seam's own dormancy (pure composition, no
    /// persistence/route/query surface) is proven below.
    /// </summary>
    [Fact]
    public void BND6_NoComparisonFolhaPerCmOrSendCarrierIsInvented()
    {
        var seamPath = P2T06ProductionScan.ComparisonCompositionSeamPath;
        var otherSources = P2T06ProductionScan.ProductionSourcePaths
            .Where(path => !string.Equals(path, seamPath, StringComparison.Ordinal))
            .ToList();

        foreach (var token in P2T06ProductionScan.DeferredCarrierTokens)
        {
            var offenders = otherSources
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The deferred-carrier token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }

        // The seam really pins the contract: it exposes the closed relation vocabulary
        // (PreviousPesoId) in CODE — the carve-out can never shelter an unrelated file.
        Assert.True(P2T04ProductionScan.Exists(seamPath), $"The CP4 seam file is missing: {seamPath}");
        var seamSource = P2T04ProductionScan.Read(seamPath);
        Assert.True(
            P2T04ProductionScan.CodeOccurrences(seamSource, "PreviousPesoId").Count > 0,
            "The CP4 seam must expose the pinned previous_peso_id member in code.");

        // The seam is dormant and pure (AC-CP4): no persistence, no route, no repository and no
        // querying surface exists in its code.
        foreach (var dormantToken in new[]
                 {
                     "Repository", "DbContext", "DbSet", "MapGet", "MapPost", "MapGroup",
                     "RequireAuthorization", "SaveAsync", "Transaction", "SqlQuery",
                     "ExecuteUpdate",
                 })
        {
            Assert.True(
                P2T04ProductionScan.CodeOccurrences(seamSource, dormantToken).Count == 0,
                $"The CP4 seam must stay dormant: the token '{dormantToken}' must not appear in {seamPath}.");
        }

        // The migration creates EXACTLY the one contracted table (and no comparison/Folha/send table).
        var created = P2T06ProductionScan.CreatedTableNames();
        Assert.Equal(1, created.Count);
        Assert.Equal("peso_review_decisions", created[0]);
    }

    // ------------------------------------------------------------------- BND7 (AC-B7)

    /// <summary>BND7 â€” no second calculation engine: no formula, no density resolution, no
    /// config re-read in any P2-T06 CODE source (review truth is the frozen Peso fact; the pages
    /// only render display LABELS of the shared model â€” not scanned here).</summary>
    [Fact]
    public void BND7_NoSecondCalculationEngineExists()
    {
        foreach (var token in P2T06ProductionScan.CalculationEngineTokens)
        {
            var offenders = P2T06ProductionScan.CodeSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The calculation-engine token '{token}' appears in code of: {string.Join(", ", offenders)}.");
        }
    }

    // ------------------------------------------------------------------- BND8 (AC-B8)

    /// <summary>BND8 â€” no generic lifecycle/revision/queue/audit infrastructure exists.</summary>
    [Fact]
    public void BND8_NoGenericLifecycleRevisionOrQueueInfrastructureExists()
    {
        foreach (var token in P2T06ProductionScan.GenericInfrastructureTokens)
        {
            var offenders = P2T06ProductionScan.ProductionSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The generic-infrastructure token '{token}' appears in P2-T06 sources: {string.Join(", ", offenders)}.");
        }
    }

    // ------------------------------------------------------------------- I4/I5 (AC-I4/AC-I5)

    /// <summary>
    /// I4 (AC-I4/AC-B9) â€” no approval-copy/fake identity exists anywhere in the P2-T06
    /// code/schema: no <c>approval_peso_id</c>, no <c>production_id</c>, no
    /// <c>job_on_revision_id</c>, no review-copy type/table/column.
    /// </summary>
    [Fact]
    public void I4_NoApprovalCopyOrFakeProductionIdentityExists()
    {
        foreach (var token in P2T06ProductionScan.LegacyIdentityTokens)
        {
            var offenders = P2T06ProductionScan.ProductionSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The legacy identity token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }

        var migration = P2T04ProductionScan.Read(P2T06ProductionScan.MigrationSourcePaths[0]);
        Assert.DoesNotContain("production_id", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("revision", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("approval_peso", migration, StringComparison.Ordinal);
    }

    /// <summary>
    /// I5 (AC-I5) â€” no P2-T06 code creates <c>cm_id</c>/<c>tool_id</c>/<c>jobon_id</c>: the
    /// anchors appear only through the consumed read-model projections (the schema carries no
    /// new cm/tool/jobon column at all).
    /// </summary>
    [Fact]
    public void I5_NoAnchorIdentityIsCreatedOrPersistedByP2T06()
    {
        var migration = P2T04ProductionScan.Read(P2T06ProductionScan.MigrationSourcePaths[0]);

        Assert.DoesNotContain("cm_id", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("tool_id", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("jobon_id", migration, StringComparison.Ordinal);
    }

    /// <summary>I6 (AC-I6) â€” exactly one new identity type exists in the P2-T06 domain surface:
    /// <c>PesoReviewDecisionId</c>; nothing else is introduced.</summary>
    [Fact]
    public void I6_ExactlyOneNewIdentityTypeExistsInTheP2T06DomainSurface()
    {
        var p2t06DomainTypes = P2T06ProductionScan.DomainSourcePaths
            .Select(P2T04ProductionScan.Read)
            .ToList();

        var identities = typeof(PesoReviewDecisionId).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "DMO.Domain.Controlo" && type.Name.Contains("PesoReview", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(4, identities.Count); // the id + the kind enum + its tokens + the decision record
        Assert.Contains(identities, type => type.Name == nameof(PesoReviewDecisionId));
        Assert.Single(identities.Where(type => type.Name.EndsWith("Id", StringComparison.Ordinal))); // exactly ONE identity type

        Assert.DoesNotContain(typeof(PesoReviewDecisionId).Assembly.GetTypes(), type =>
            type.Namespace == "DMO.Domain.Controlo"
            && (type.Name.Contains("ApprovalPeso", StringComparison.Ordinal)
                || type.Name.Contains("ReviewId", StringComparison.Ordinal)
                || type.Name.Contains("SendId", StringComparison.Ordinal)));
    }

    // ------------------------------------------------------------------- D2/D3 (AC-D1/AC-D2/AC-D3)

    /// <summary>
    /// D2 (AC-D1/AC-D2) â€” no code path derives a decision from warnings/results/configuration:
    /// the decision commands carry only identity/version/reason and no auto-approve/auto-reject
    /// symbol or threshold logic exists in any P2-T06 code source.
    /// </summary>
    [Fact]
    public void D2_NoDecisionIsEverDerivedAutomatically()
    {
        foreach (var token in new[] { "AutoApprove", "AutoReject", "Threshold", "auto_approve", "auto_reject" })
        {
            var offenders = P2T06ProductionScan.CodeSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The auto-decision token '{token}' appears in code of: {string.Join(", ", offenders)}.");
        }

        // The command carriers accept no Peso facts (closed shape, D1 cross-check).
        Assert.Equal(2, typeof(ApprovePesoCommand).GetProperties().Length);
        Assert.DoesNotContain(typeof(ApprovePesoCommand).GetProperties(), p => p.Name.Contains("Rows", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(ApprovePesoCommand).GetProperties(), p => p.Name.Contains("Density", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(ApprovePesoCommand).GetProperties(), p => p.Name.Contains("Warning", StringComparison.Ordinal));
    }

    /// <summary>
    /// D3 (AC-D3) â€” the decision trail uses exactly <c>aprovado</c>/<c>nao_aprovado</c>/
    /// <c>reaberto</c> (domain tokens + DB CHECK) and the per-CM vocabulary is exactly
    /// <c>Manter</c>/<c>Colocar de parte</c> â€” documented constants only, never persistence
    /// (no additional decision word exists anywhere).
    /// </summary>
    [Fact]
    public void D3_TheClosedDecisionAndPerCmVocabulariesArePinned()
    {
        // The decision tokens exist in the domain vocabulary and the migration CHECK.
        var tokenSource = P2T04ProductionScan.Read(P2T06ProductionScan.DomainSourcePaths[1]);
        var migration = P2T04ProductionScan.Read(P2T06ProductionScan.MigrationSourcePaths[0]);

        foreach (var token in P2T06ProductionScan.OwnedDecisionTokens)
        {
            Assert.Contains(token, tokenSource, StringComparison.Ordinal);
            Assert.Contains(token, migration, StringComparison.Ordinal);
        }

        // The per-CM vocabulary lives ONLY in the documented constants type (Q-PERCM: vocabulary
        // fixed, carrier deferred) â€” never in tables/routes/types/prose elsewhere.
        var vocabularySource = P2T04ProductionScan.Read("src/DMO.Application/Controlo/Approve/PerCmDecisionVocabulary.cs");
        Assert.Contains("\"Manter\"", vocabularySource, StringComparison.Ordinal);
        Assert.Contains("\"Colocar de parte\"", vocabularySource, StringComparison.Ordinal);
        Assert.Contains("const string Manter", vocabularySource, StringComparison.Ordinal);
        Assert.Contains("const string ColocarDeParte", vocabularySource, StringComparison.Ordinal);

        var otherPerCmMentions = P2T06ProductionScan.ProductionSourcePaths
            .Where(path => !path.Contains("PerCmDecisionVocabulary", StringComparison.Ordinal))
            .Where(path => P2T06ProductionScan.PerCmVocabularyTokens.Any(token =>
                P2T04ProductionScan.CodeOccurrences(P2T04ProductionScan.Read(path), token).Count > 0))
            .ToList();

        Assert.True(
            otherPerCmMentions.Count == 0,
            $"The per-CM vocabulary appears outside its documented constants type in: {string.Join(", ", otherPerCmMentions)}.");
    }

    // ------------------------------------------------------------------- CP1 (AC-CP1)

    /// <summary>CP1 â€” no heuristic previous-Peso logic exists: no latest/date/reference/machine
    /// selection, no reconstruction, no default pairing anywhere in the P2-T06 code sources.</summary>
    [Fact]
    public void CP1_NoHeuristicPreviousPesoLogicExists()
    {
        foreach (var token in new[] { "latest", "LatestPeso", "PreviousWeight", "pair", "Pairing", "reconstruct" })
        {
            var offenders = P2T06ProductionScan.CodeSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The heuristic-pairing token '{token}' appears in code of: {string.Join(", ", offenders)}.");
        }
    }

    // ------------------------------------------------------------------- R2/O5 (AC-R1/AC-O5)

    /// <summary>
    /// R2/O5 â€” the P2-T06 write paths touch ONLY <c>pesos.status</c>/<c>version</c>/
    /// <c>updated_at</c>/<c>submitted_at</c>/<c>submitted_by_user_id</c> (+ the decision table):
    /// the repository applies the transitions on exactly those members and no other
    /// <c>pesos</c>/row column is ever written.
    /// </summary>
    [Fact]
    public void R2O5_TheDecisionWriteTouchesOnlyLifecycleColumnsAndTheTrail()
    {
        var repository = P2T04ProductionScan.Read("src/DMO.Infrastructure/Persistence/Controlo/PesoReviewRepository.cs");

        // The only entity writes of the decision path are the contracted lifecycle members.
        Assert.Contains("peso.Status =", repository, StringComparison.Ordinal);
        Assert.Contains("peso.Version += 1", repository, StringComparison.Ordinal);
        Assert.Contains("peso.UpdatedAt = now", repository, StringComparison.Ordinal);
        Assert.Contains("peso.SubmittedAt = null", repository, StringComparison.Ordinal);
        Assert.Contains("peso.SubmittedByUserId = null", repository, StringComparison.Ordinal);

        // // No measurement-row WRITE path exists: the decision write inserts only the Peso transition
        // and the decision row — no row add/remove and no Peso input assignment anywhere.
        Assert.DoesNotContain("InsertRow", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveRange", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("WaterTemperature =", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("GlassDensityGCm3 =", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("VolumeMarisaBq =", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("Decisions.Update", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("Decisions.Remove", repository, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------- A5 (AC-A1)

    /// <summary>
    /// A5 â€” every P2-T06 route/action carries exactly the canonical <c>controlo-approve</c>
    /// policy: the pinned page constant equals the canonical projection; no second policy exists.
    /// </summary>
    [Fact]
    public void A5_EveryP2T06RouteCarriesExactlyTheCanonicalControloApprovePolicy()
    {
        Assert.True(ControloApprovePolicyNames.MatchesCanonicalPolicy());
        Assert.Equal("dmo.module.controlo-approve", ControloApprovePolicyNames.ControloApprove);
        Assert.Equal(
            ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.ControloApprove),
            ControloApproveEndpoints.Policy);

        // The endpoint mapping methods declare exactly the one group-level policy.
        var endpointSource = P2T04ProductionScan.Read("src/DMO.Web/Endpoints/Controlo/ControloApproveEndpoints.cs");
        Assert.Contains("RequireAuthorization(Policy)", endpointSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RequireAuthorization(\"dmo.module.controlo-create\")", endpointSource, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------- L1/L3 (AC-L1/AC-L3) fixed desktop

    /// <summary>
    /// L1 â€” the P2-T06-owned assets carry no breakpoint/container/support structural rule, no
    /// width listener and no card conversion (the fixed 1366 Ã— 768 composition).
    /// </summary>
    [Fact]
    public void L1_NoBreakpointOrStructuralReflowRuleExistsInP2T06Assets()
    {
        foreach (var asset in P2T06ProductionScan.AssetPaths)
        {
            var content = asset.EndsWith(".css", StringComparison.Ordinal)
                ? P2T04ProductionScan.WithoutCssComments(P2T04ProductionScan.Read(asset))
                : P2T04ProductionScan.Read(asset);

            Assert.DoesNotContain("@media", content, StringComparison.Ordinal);
            Assert.DoesNotContain("@container", content, StringComparison.Ordinal);
            Assert.DoesNotContain("@supports", content, StringComparison.Ordinal);
            Assert.DoesNotContain("matchMedia", content, StringComparison.Ordinal);
            Assert.DoesNotContain("innerWidth", content, StringComparison.Ordinal);
            Assert.DoesNotContain("resize", content, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// L3 â€” no shared shell/header/navigation file is modified: the P2-T06 pages render inside
    /// the existing shell; the protected shared paths carry no P2-T06 vocabulary.
    /// </summary>
    [Fact]
    public void L3_NoSharedShellOrNavigationFileIsModified()
    {
        foreach (var protectedPath in new[]
                 {
                     "src/DMO.Web/Frontend/Shell/DestinationRoutes.cs",
                     "src/DMO.Web/Navigation/DestinationRouteRegistrations.cs",
                     "src/DMO.Web/Pages/Shared/_Layout.cshtml",
                 })
        {
            var source = P2T04ProductionScan.Read(protectedPath);
            Assert.DoesNotContain("approve", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("historico", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------------------------- App. A / protected files

    /// <summary>
    /// MG1 (App. A / AC-MG1) â€” migrations 001â€“005 (incl. Designers) and <c>DmoDbContext.cs</c>
    /// are byte-identical (normalized content hash); the P2-T05 protected surface is untouched.
    /// </summary>
    [Fact]
    public void MG1_ProtectedFilesAreByteIdentical()
    {
        foreach (var (path, expected) in P2T06ProductionScan.PinnedProtectedFoundation)
        {
            var actual = P2T04ProductionScan.HashFile(path);
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>S2 â€” no availability/navigation registration token exists in any P2-T06 source.</summary>
    [Fact]
    public void BND4_NoAvailabilityOrNavigationRegistrationExists()
    {
        foreach (var token in P2T06ProductionScan.AvailabilityTokens)
        {
            var offenders = P2T06ProductionScan.ProductionSourcePaths
                .Where(path => P2T04ProductionScan.CodeOccurrences(
                    P2T04ProductionScan.Read(path),
                    token).Count > 0)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The availability/navigation token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }
    }

    /// <summary>B5/H4 cross-check â€” the Approve pages never link or name <c>historia</c> (the
    /// comment-aware scan keeps the documented HISTÃ“RICO-GLOBAL boundary remark out of code).</summary>
    [Fact]
    public void B5_TheApprovePagesNeverLinkOrNameHistoricoGlobal()
    {
        foreach (var page in P2T04ProductionScan.FilesUnder("src/DMO.Web/Pages/Controlo/Approve", ".cshtml"))
        {
            var source = P2T04ProductionScan.Read(page);
            Assert.Equal(0, P2T04ProductionScan.CodeOccurrences(source, "historia").Count);
            Assert.Equal(0, P2T04ProductionScan.CodeOccurrences(source, "/historia").Count);
            Assert.Equal(0, P2T04ProductionScan.CodeOccurrences(source, "HISTÃ“RICO GLOBAL").Count);
        }
    }
}