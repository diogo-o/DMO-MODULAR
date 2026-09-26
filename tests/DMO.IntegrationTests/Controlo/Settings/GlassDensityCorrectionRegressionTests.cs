using System.Text.RegularExpressions;
using DMO.Application.Access;
using DMO.IntegrationTests.JobOn;
using DMO.Web.Authorization;

namespace DMO.IntegrationTests.Controlo.Settings;

/// <summary>
/// P2-T05 post-closure correction — boundary/regression (<c>S</c>) proofs of the glass-density
/// correction slice: exactly one new migration/table, the removed configuration authority, no
/// per-<c>tool_id</c> density anywhere, routes gated, availability untouched and no later
/// workstream (P2-T06/07/08/10) implementation appears in the new sources.
/// </summary>
/// <remarks>
/// Authority: correction contract §5.6 (migration delta), §5.7 test row S (no per-tool density
/// token; <c>Controlo:Calculation:GlassDensities</c> not read; exactly one new migration;
/// no ninth table beyond <c>glass_density_settings</c>; no availability registration) and §6.2
/// (closed-decision preservation). <c>CurrentBuildAvailable</c> staying <c>[]</c> is the
/// unchanged accepted row BND1 (still green); it is re-pinned here for the correction record.
/// </remarks>
public sealed class GlassDensityCorrectionRegressionTests
{
    /// <summary>The single correction migration and its designer (the FIFTH migration overall —
    /// Architect review observation N-1: the repo holds four migrations before this correction).</summary>
    private const string CorrectionMigrationSource =
        "src/DMO.Infrastructure/Migrations/20260923122429_GlassDensitySettings.cs";

    /// <summary>
    /// GD-S1 — the correction migration creates EXACTLY the one approved table
    /// (<c>glass_density_settings</c>) and nothing else; its <c>Down</c> drops exactly that table
    /// (no unrelated schema drift); and the migration carries no later-workstream vocabulary.
    /// </summary>
    [Fact]
    public void GD_S1_TheCorrectionMigrationIsExactlyOneTableAndNothingElse()
    {
        var source = P2T04ProductionScan.Read(CorrectionMigrationSource);

        var created = Regex.Matches(source, @"CreateTable\(\s*name:\s*""(?<name>[a-z_]+)""")
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(new[] { "glass_density_settings" }, created);

        const string downMarker = "protected override void Down(MigrationBuilder migrationBuilder)";
        var downStart = source.IndexOf(downMarker, StringComparison.Ordinal);
        Assert.True(downStart >= 0, "The correction migration has no Down method.");

        var dropped = Regex.Matches(source[downStart..], @"DropTable\(\s*name:\s*""(?<name>[a-z_]+)""")
            .Select(match => match.Groups["name"].Value)
            .ToList();
        Assert.Equal(new[] { "glass_density_settings" }, dropped);

        // The migration is additive: no ALTER/CREATE INDEX/other DDL statement exists beyond the
        // one CreateTable + the two seed rows.
        Assert.DoesNotContain("DropColumn", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddColumn", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateIndex", source, StringComparison.Ordinal);

        // The two provenance-backed seed rows: NNPB 2.4027 / PS 2.4231, version 1 — exactly ONE
        // InsertData statement carrying the two-row values array (no re-seed exists anywhere).
        var inserts = Regex.Matches(source, @"InsertData\(");
        Assert.Single(inserts);
        Assert.Contains("{ \"NNPB\", 2.4027m, 1 }", source, StringComparison.Ordinal);
        Assert.Contains("{ \"PS\", 2.4231m, 1 }", source, StringComparison.Ordinal);

        // No later workstream implementation in the migration source.
        foreach (var token in new[]
                 {
                     "approval", "approve", "pdf", "email_send", "boquilhas", "availability",
                 })
        {
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// GD-S2 — the former deployment configuration section <c>Controlo:Calculation:GlassDensities</c>
    /// lost authority: the literal appears in no CODE of any <c>src</c> file (no dual source of
    /// truth; the glass density resolves only from the operational settings store). Documentation
    /// remarks that name the removed section are allowed (comment-aware scan, the same convention
    /// as the accepted vocabulary rows); no code path reads it.
    /// </summary>
    [Fact]
    public void GD_S2_TheFormerGlassDensityConfigurationSectionIsNowhereInTheCodebase()
    {
        var offenders = new List<string>();

        foreach (var path in P2T04ProductionScan.FilesUnder("src", ".cs", ".cshtml", ".css", ".js", ".json"))
        {
            var codeTokens = P2T04ProductionScan.CodeOccurrences(
                P2T04ProductionScan.Read(path),
                "Controlo:Calculation:GlassDensities");

            if (codeTokens.Count > 0)
            {
                offenders.Add(path);
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"The removed configuration section is still READ in code: {string.Join(", ", offenders)}.");
    }

    /// <summary>
    /// GD-S3 — no per-<c>tool_id</c> density exists anywhere: the Tool facts expose no density
    /// member, the <c>tools</c> migration declares no density column, and the glass-density
    /// setting/entity carry no Tool identity. The Tool carries ONLY the processo classification
    /// (Owner rule R1).
    /// </summary>
    [Fact]
    public void GD_S3_NoPerToolDensityPersistenceExists()
    {
        // The Tool domain/entity never expose a density fact.
        var toolFicha = P2T04ProductionScan.Read("src/DMO.Application/Tools/ToolModels.cs");
        var toolEntity = P2T04ProductionScan.Read("src/DMO.Infrastructure/Persistence/ToolJobOn/Entities/ToolEntity.cs");

        Assert.DoesNotContain("Density", toolFicha, StringComparison.Ordinal);
        Assert.DoesNotContain("Density", toolEntity, StringComparison.Ordinal);

        // The P2-T04 tools migration carries no density column.
        var toolsMigration = P2T04ProductionScan.Read(
            "src/DMO.Infrastructure/Migrations/20260922232349_ToolJobOnDomainCore.cs");
        Assert.DoesNotContain("density", toolsMigration, StringComparison.Ordinal);

        // The glass-density setting carries the processo token + density + concurrency facts
        // ONLY — no Tool id, no cm id, no per-reference dimension.
        var domainSetting = P2T04ProductionScan.Read("src/DMO.Domain/Controlo/GlassDensitySetting.cs");
        var entitySetting = P2T04ProductionScan.Read(
            "src/DMO.Infrastructure/Persistence/Controlo/Entities/GlassDensitySettingEntity.cs");

        foreach (var source in new[] { domainSetting, entitySetting })
        {
            Assert.DoesNotContain("ToolId", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CmId", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Reference", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Lot", source, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// GD-S4 — availability stays untouched: <c>ModuleRegistrations.CurrentBuildAvailable</c>
    /// remains <c>[]</c> after the correction (P2-T10 owns availability; the correction registers
    /// no Module, no destination and no navigation entry).
    /// </summary>
    [Fact]
    public void GD_S4_CurrentBuildAvailableRemainsEmpty()
    {
        Assert.NotNull(ModuleRegistrations.CurrentBuildAvailable);
        Assert.Empty(ModuleRegistrations.CurrentBuildAvailable);

        // The new routes belong to the EXISTING Definições group: no new RequireAuthorization
        // policy declaration and no approve/admin policy appears in the endpoints file.
        var endpoints = P2T04ProductionScan.Read("src/DMO.Web/Endpoints/Controlo/ControloDefinicoesEndpoints.cs");
        var policies = Regex.Matches(endpoints, @"RequireAuthorization\((?<policy>[^)]+)\)")
            .Select(match => match.Groups["policy"].Value)
            .ToList();

        Assert.All(policies, policy =>
        {
            Assert.DoesNotContain("Approve", policy, StringComparison.Ordinal);
            Assert.DoesNotContain("administration", policy, StringComparison.Ordinal);
        });

        Assert.Single(policies); // the single group-level canonical gate
    }
}