using DMO.IntegrationTests.JobOn;

namespace DMO.IntegrationTests.Boquilhas;

/// <summary>
/// P2-T07 boundary/regression scan helper: the P2-T07 production path tables, the forbidden
/// vocabulary and the protected-file registers used by the boundary rows <c>P2T07RegressionTests</c>
/// (contract §26 BND-B1…BND-B10 and §29 rows N1–N7/A6/R4/H2).
/// </summary>
/// <remarks>
/// The forbidden-vocabulary tables are composed from parts, exactly like the accepted
/// <c>P2T04ProductionScan</c>/<c>P2T05ProductionScan</c> helpers, so this assertion source never
/// carries a composed literal of its own. The comment-aware scan discipline is the shared
/// <see cref="P2T04ProductionScan.CodeOccurrences"/> rule.</remarks>
internal static class P2T07ProductionScan
{
    // ================= P2-T07 production sources (contract Appendix B) =====================

    /// <summary>The new P2-T07 domain sources.</summary>
    public static IReadOnlyList<string> DomainSourcePaths { get; } =
        P2T04ProductionScan.FilesUnder("src/DMO.Domain/Boquilhas", ".cs");

    /// <summary>The new P2-T07 application sources.</summary>
    public static IReadOnlyList<string> ApplicationSourcePaths { get; } =
        P2T04ProductionScan.FilesUnder("src/DMO.Application/Boquilhas", ".cs")
            .Concat(
            [
                "src/DMO.Application/Repositories/IBoquilhasRepository.cs",
                "src/DMO.Application/Persistence/BoquilhasPersistenceException.cs",
            ])
            .ToList();

    /// <summary>The new P2-T07 persistence sources (OWNER CLARIFICATION: THREE entities/configs —
    /// register, movement, audit; the machine set, close snapshot and reopening structures are
    /// superseded and removed).</summary>
    public static IReadOnlyList<string> PersistenceSourcePaths { get; } =
    [
        "src/DMO.Infrastructure/Persistence/Boquilhas/BoquilhasRepository.cs",
        "src/DMO.Infrastructure/Persistence/Boquilhas/BoquilhasDependencyProbe.cs",
        "src/DMO.Infrastructure/Persistence/Boquilhas/DmoBoquilhasContextRead.cs",
        "src/DMO.Infrastructure/Persistence/Boquilhas/Entities/BoquilhaEntity.cs",
        "src/DMO.Infrastructure/Persistence/Boquilhas/Entities/BoquilhaMovementEntity.cs",
        "src/DMO.Infrastructure/Persistence/Boquilhas/Entities/BoquilhaMovementAuditEntity.cs",
        "src/DMO.Infrastructure/Persistence/Boquilhas/EntityConfigurations/BoquilhaEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/Boquilhas/EntityConfigurations/BoquilhaMovementEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/Boquilhas/EntityConfigurations/BoquilhaMovementAuditEntityConfiguration.cs",
    ];

    /// <summary>The single P2-T07 migration and its EF designer (the OWNER-CLARIFICATION corrected
    /// pair replacing the unreviewed 007; migration 007 is not closed, so the schema was corrected
    /// cleanly — no compensating legacy migration exists).</summary>
    public static IReadOnlyList<string> MigrationSourcePaths { get; } =
    [
        "src/DMO.Infrastructure/Migrations/20260924051151_BoquilhasDomain.cs",
        "src/DMO.Infrastructure/Migrations/20260924051151_BoquilhasDomain.Designer.cs",
    ];

    /// <summary>The P2-T07 Web surfaces (incl. the §34.3 Boquilhas Definições endpoint surface).</summary>
    public static IReadOnlyList<string> WebSourcePaths { get; } =
        P2T04ProductionScan.FilesUnder("src/DMO.Web/Pages/Boquilhas", ".cs", ".cshtml")
            .Concat(
            [
                "src/DMO.Web/Endpoints/BoquilhasEndpoints.cs",
                "src/DMO.Web/Endpoints/BoquilhasDefinicoesEndpoints.cs",
            ])
            .ToList();

    /// <summary>The two new P2-T07 assets.</summary>
    public static IReadOnlyList<string> AssetPaths { get; } =
    [
        "src/DMO.Web/wwwroot/css/dmo-boquilhas.css",
        "src/DMO.Web/wwwroot/js/dmo-boquilhas.js",
    ];

    /// <summary>Every P2-T07 production source scanned by the vocabulary rows.</summary>
    public static IReadOnlyList<string> ProductionSourcePaths { get; } =
        DomainSourcePaths
            .Concat(ApplicationSourcePaths)
            .Concat(PersistenceSourcePaths)
            .Concat(MigrationSourcePaths)
            .Concat(WebSourcePaths)
            .Concat(AssetPaths)
            .ToList();

    /// <summary>
    /// The code-only P2-T07 sources EXCLUDING the EF-generated migration Designer: the Designer is
    /// a full-model snapshot and legitimately repeats every earlier table name (the accepted scan
    /// discipline — the mechanical vocabulary rows measure P2-T07-authored code, not the EF mirror).
    /// </summary>
    public static IReadOnlyList<string> CodeSourcePaths { get; } = ProductionSourcePaths
        .Where(path => path.EndsWith(".cs", StringComparison.Ordinal))
        .Where(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal))
        .ToList();

    // ================= the accepted non-new files P2-T07 changes (App. A) ===================

    /// <summary>The only accepted non-new files P2-T07 changes (contract App. A, last paragraph).</summary>
    public static IReadOnlyList<string> DocumentedAdditiveSourcePaths { get; } =
    [
        "src/DMO.Web/Program.cs",
        "src/DMO.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs",
        "src/DMO.Infrastructure/Migrations/DmoDbContextModelSnapshot.cs",
    ];

    /// <summary>The P2-T07 owned source path prefixes (contract Appendix B).</summary>
    public static IReadOnlyList<string> OwnedPathPrefixes { get; } =
    [
        "src/DMO.Domain/Boquilhas/",
        "src/DMO.Application/Boquilhas/",
        "src/DMO.Application/Repositories/IBoquilhas",
        "src/DMO.Application/Persistence/BoquilhasPersistenceException.cs",
        "src/DMO.Infrastructure/Persistence/Boquilhas/Boquilhas",
        "src/DMO.Infrastructure/Persistence/Boquilhas/Entities/Boquilha",
        "src/DMO.Infrastructure/Persistence/Boquilhas/EntityConfigurations/Boquilha",
        "src/DMO.Infrastructure/Migrations/20260924051151_BoquilhasDomain",
        "src/DMO.Web/Pages/Boquilhas/",
        "src/DMO.Web/Endpoints/BoquilhasEndpoints.cs",
        "src/DMO.Web/Endpoints/BoquilhasDefinicoesEndpoints.cs",
        "src/DMO.Web/wwwroot/css/dmo-boquilhas.css",
        "src/DMO.Web/wwwroot/js/dmo-boquilhas.js",
    ];

    /// <summary>Whether the supplied relative path belongs to the P2-T07 owned surface.</summary>
    public static bool IsOwnedPath(string relativePath) =>
        OwnedPathPrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.Ordinal))
        || DocumentedAdditiveSourcePaths.Contains(relativePath, StringComparer.Ordinal);

    // ================= forbidden vocabulary (composed from parts) ============================

    /// <summary>The P2-T08 execution tokens (BND-B2/N2): no PDF/file/email mechanics.</summary>
    public static IReadOnlyList<string> P2T08ExecutionTokens { get; } =
    [
        string.Concat("P", "df"),
        string.Concat("Pdf", "Directory"),
        string.Concat("I", "Email", "Service"),
        string.Concat("Smtp", "Client"),
        string.Concat("Smtp", "Server"),
        string.Concat("File.", "Write"),
        string.Concat("File.", "Create"),
        string.Concat("Directory.", "Create"),
        string.Concat("Document", "Id"),
        string.Concat("Email", "Send"),
    ];

    /// <summary>The HISTÓRICO GLOBAL tokens (BND-B3/H2/N3): no <c>historia</c> route/entry.</summary>
    public static IReadOnlyList<string> HistoriaTokens { get; } =
    [
        string.Concat("histor", "ia"),
        string.Concat("Historico", "Global"),
        string.Concat("Hist", "\u00f3rico", " Global"),
    ];

    /// <summary>The settings/administration tokens of the §34.3 boundary (N1/R4): Boquilhas owns the
    /// repairer family (<c>Boquilhas > Definições</c> — the repairer register + the machine
    /// assignments, moved from Controlo by the Owner clarification), so the old "no repairer
    /// administration" tokens are SUPERSEDED and removed; what remains forbidden in the Boquilhas
    /// sources is the CONTROL settings surface (PDF directory, email lists/templates — still
    /// Controlo-owned) and the glass-density settings (Controlo).</summary>
    public static IReadOnlyList<string> SettingsTokens { get; } =
    [
        string.Concat("GlassDensity", "Settings"),
        string.Concat("PdfDirectory", "Settings"),
        string.Concat("EmailList"),
        string.Concat("EmailTemplate"),
    ];

    /// <summary>The false-identity tokens (BND-B8/I4): no production_id/job_on_revision_id/fake
    /// bq_id/fake jobon_id/reverse arrays/per-piece UUID.</summary>
    public static IReadOnlyList<string> FalseIdentityTokens { get; } =
    [
        string.Concat("production", "_id"),
        string.Concat("production", "Id"),
        string.Concat("job", "_on_revision", "_id"),
        string.Concat("jobOn", "Revision"),
        string.Concat("revision", "Id"),
        string.Concat("Machine", "Id"),
        string.Concat("machine", "_id"),
    ];

    /// <summary>The superseded lifecycle tokens (OWNER CLARIFICATION): no close/reopen/status/
    /// snapshot/reopening/active machinery exists anywhere in the P2-T07 surface (the B1
    /// active-anchor machinery is superseded and removed).</summary>
    public static IReadOnlyList<string> LifecycleTokens { get; } =
    [
        string.Concat("ActiveAggregate", "Exists"),
        string.Concat("IX", "_boquilhas", "_active", "_tool", "_id"),
        string.Concat("IX", "_boquilhas", "_active", "_bq", "_id"),
        string.Concat("boquilha_close", "_snapshots"),
        string.Concat("boquilha_reopen", "ings"),
        string.Concat("boquilha_mach", "ines"),
        string.Concat("CloseSnapshot", "Async"),
        string.Concat("Reopening", "Record"),
        string.Concat("OnlyOneIn", "icio"),
        string.Concat("Irreparavel", "Exceeds", "InRepair"),
        string.Concat("Saida", "Exceeds", "Available"),
        string.Concat("Already", "Closed"),
        string.Concat("NotLast", "Closed"),
        string.Concat("HasActiveAggregate", "ForAnchor"),
        string.Concat("UpdateOpeningFacts", "Async"),
        string.Concat("ReopenBoquilhas", "Command"),
        string.Concat("CloseBoquilhas", "Command"),
    ];

    /// <summary>The second-balance-authority tokens (BND-B7/B1): no balance table/column in the
    /// schema; the outstanding value is derived by replay at read time and never stored.</summary>
    public static IReadOnlyList<string> BalanceTokens { get; } =
    [
        string.Concat("balance", "s"),
        string.Concat("balance", "_table"),
        string.Concat("Saldo", "Total"),
        string.Concat("Stored", "Balance"),
        string.Concat("expected_return", "_quantity"),
        string.Concat("excess_received", "_quantity"),
    ];

    /// <summary>The movement-vocabulary leakage tokens (BND-B6/N6/V1): no fifth/legacy type and no
    /// annulment/delete path.</summary>
    public static IReadOnlyList<string> MovementLeakageTokens { get; } =
    [
        string.Concat("cont", "agem"),
        string.Concat("Fabric", "ar/Reparar"),
        string.Concat("annul", "ed"),
        string.Concat("DeleteMovement", "Async"),
        string.Concat("RemoveMovement", "Async"),
    ];

    /// <summary>The P2-T06/P2-T05 leakage tokens (BND-B9): no decision trail/Peso/approval code.</summary>
    public static IReadOnlyList<string> ControloLeakageTokens { get; } =
    [
        string.Concat("Peso", "Review"),
        string.Concat("Decision", "Trail"),
        string.Concat("Controlo", "Approve"),
        string.Concat("Aprovar"),
        string.Concat("peso_review", "_decisions"),
    ];

    // ================= test-surface registers (BND10-style enumeration) ======================

    /// <summary>The new P2-T07 test folders (excluded from the "existing tests" scans of the
    /// earlier workstreams, same treatment the P2-T04/P2-T05/P2-T06 helpers gave their own).</summary>
    public static IReadOnlyList<string> NewP2T07TestFolders { get; } =
    [
        "tests/DMO.UnitTests/Boquilhas/",
        "tests/DMO.IntegrationTests/Boquilhas/",
    ];

    /// <summary>The new P2-T07 env-gated persistence test files.</summary>
    public static IReadOnlyList<string> NewP2T07PersistenceTestFiles { get; } =
    [
        "Migration007BoquilhasDomainTests.cs",
        "BoquilhasRepositoryIntegrationTests.cs",
    ];

    /// <summary>The disclosed additive test paths this workstream extends (the accepted pin-growth
    /// discipline: inventory rows grow by the six tables, the seventh migration and the Boquilhas
    /// access surface).</summary>
    public static IReadOnlyList<string> DisclosedAdditiveTestPaths { get; } =
    [
        // P2-T04's disclosed list is extended by P2-T07's own new folders and files.
        "tests/DMO.IntegrationTests/Persistence/MigrationRunnerTests.cs",
        "tests/DMO.IntegrationTests/Persistence/DatabaseConnectivityTests.cs",
        // The Job On Tool-restriction inventory and the P2-T05 access inventory are recorded by
        // those workstreams' own regression pins; P2-T07's additions are disclosed in
        // P2T04ProductionScan (the shared helper registers the new Boquilhas test surface).
    ];

    /// <summary>Returns whether a repository-relative test path belongs to the new P2-T07 surface.</summary>
    public static bool IsNewP2T07TestPath(string relativePath) =>
        NewP2T07TestFolders.Any(folder => relativePath.StartsWith(folder, StringComparison.Ordinal))
        || Path.GetFileName(relativePath).StartsWith("P2T07", StringComparison.OrdinalIgnoreCase)
        || NewP2T07PersistenceTestFiles.Contains(Path.GetFileName(relativePath), StringComparer.Ordinal);
}