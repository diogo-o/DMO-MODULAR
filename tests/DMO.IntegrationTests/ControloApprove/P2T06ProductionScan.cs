using DMO.IntegrationTests.JobOn;

namespace DMO.IntegrationTests.ControloApprove;

/// <summary>
/// P2-T06 boundary/regression scan helper: the P2-T06 production path tables and the
/// ownership/vocabulary tables used by the boundary rows BND1–BND8, I4/I5, D2/D3/D6, CP1/CP2,
/// R2, O5, H4, A5 and L1/L3.
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract §7.3 (index tables), §13/§14 (routes/policies), §22 (negative-scope
/// protections), §25 (migration contract), §26.4 and App. A (protected boundaries). Repository
/// access and the comment-aware scanning machinery are reused from the accepted
/// <see cref="P2T04ProductionScan"/> (same assembly). This helper carries no test and asserts
/// nothing: the rows live in <c>P2T06RegressionTests</c>.</remarks>
internal static class P2T06ProductionScan
{
    // ================= P2-T06 production sources ==========================================

    /// <summary>The new P2-T06 domain sources (contract Appendix B).</summary>
    public static IReadOnlyList<string> DomainSourcePaths { get; } =
    [
        "src/DMO.Domain/Controlo/PesoReviewDecisionId.cs",
        "src/DMO.Domain/Controlo/PesoReviewDecisionKind.cs",
        "src/DMO.Domain/Controlo/PesoReviewDecision.cs",
    ];

    /// <summary>The new P2-T06 application sources (contract Appendix B).</summary>
    public static IReadOnlyList<string> ApplicationSourcePaths { get; } =
        P2T04ProductionScan.FilesUnder("src/DMO.Application/Controlo/Approve", ".cs")
            .Concat(
            [
                "src/DMO.Application/Repositories/IPesoReviewRepository.cs",
                "src/DMO.Application/Persistence/PesoReviewPersistenceException.cs",
            ])
            .ToList();

    /// <summary>The new P2-T06 persistence sources (contract Appendix B).</summary>
    public static IReadOnlyList<string> PersistenceSourcePaths { get; } =
    [
        "src/DMO.Infrastructure/Persistence/Controlo/PesoReviewRepository.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/PesoReviewDecisionEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/PesoReviewDecisionEntityConfiguration.cs",
    ];

    /// <summary>The P2-T06 migration and its EF designer (contract §25.1).</summary>
    public static IReadOnlyList<string> MigrationSourcePaths { get; } =
    [
        "src/DMO.Infrastructure/Migrations/20260923171223_ControloApproveDomain.cs",
        "src/DMO.Infrastructure/Migrations/20260923171223_ControloApproveDomain.Designer.cs",
    ];

    /// <summary>The table names the P2-T06 migration actually creates, read from its own
    /// <c>CreateTable(name: "…")</c> calls, in ordinal order. Exactly the one contracted table.</summary>
    public static IReadOnlyList<string> CreatedTableNames() =>
        System.Text.RegularExpressions.Regex.Matches(
                P2T04ProductionScan.Read(MigrationSourcePaths[0]),
                @"CreateTable\(\s*name:\s*""(?<name>[A-Za-z0-9_]+)""")
            .Select(match => match.Groups["name"].Value)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>The new P2-T06 Web sources (contract Appendix B).</summary>
    public static IReadOnlyList<string> WebSourcePaths { get; } =
        P2T04ProductionScan.FilesUnder("src/DMO.Web/Pages/Controlo/Approve", ".cs", ".cshtml")
            .Concat(["src/DMO.Web/Endpoints/Controlo/ControloApproveEndpoints.cs"])
            .ToList();

    /// <summary>The new P2-T06 assets (contract §21.3).</summary>
    public static IReadOnlyList<string> AssetPaths { get; } =
    [
        "src/DMO.Web/wwwroot/css/dmo-controlo-approve.css",
        "src/DMO.Web/wwwroot/js/dmo-controlo-approve.js",
    ];

    /// <summary>
    /// Every P2-T06 production source scanned by the vocabulary rows (pages carry display hooks
    /// that legitimately contain result/density LABELS — the boundary scans that target code
    /// semantics use <see cref="CodeSourcePaths"/>).
    /// </summary>
    public static IReadOnlyList<string> ProductionSourcePaths { get; } =
        DomainSourcePaths
            .Concat(ApplicationSourcePaths)
            .Concat(PersistenceSourcePaths)
            .Concat(MigrationSourcePaths)
            .Concat(WebSourcePaths)
            .Concat(AssetPaths)
            .Concat(["src/DMO.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs", "src/DMO.Web/Program.cs"])
            .ToList();

    /// <summary>The code-only P2-T06 sources (no pages/assets): the mechanical engine/write-path scans.</summary>
    public static IReadOnlyList<string> CodeSourcePaths { get; } = ProductionSourcePaths
        .Where(path => path.EndsWith(".cs", StringComparison.Ordinal))
        .ToList();

    /// <summary>
    /// The single CP4 composition-seam file (contract §26.4 row CP4): the documented
    /// application-level pin of the future Comparação carrier composition. This is the ONLY
    /// P2-T06 production file allowed to carry the closed relation vocabulary (e.g.
    /// <c>PreviousPesoId</c>) — it IS the contracted pin, not an invented carrier; BND6 proves
    /// every other P2-T06 source stays free of the deferred-carrier tokens and proves the
    /// seam's dormancy (pure composition: no persistence/route/query surface). AC-B6 (no
    /// fabricated carrier table/column/route/type) is unchanged.
    /// </summary>
    public static string ComparisonCompositionSeamPath { get; } =
        "src/DMO.Application/Controlo/Approve/ComparisonComposer.cs";

    // ================= boundary vocabularies ===============================================

    /// <summary>
    /// P2-T08 executions still FORBIDDEN everywhere in the P2-T06 area (BND-B2, updated by the
    /// documents slice): email sending/routing, attachments, document identity, regeneration and
    /// raw file-stream mechanics. (The document-TYPE vocabulary 'peso'/'pegamentos'/'resumo' is
    /// closed P2-T05 migration content — vocabulary, not execution — and is not scanned here.)
    /// </summary>
    public static IReadOnlyList<string> P2T08ForbiddenExecutionTokens { get; } =
    [
        "SmtpClient", "MailMessage", "SendMail", "SendEmail", "EmailSender",
        "attachment", "Attachment", "DocumentId", "document_id", "sent_at",
        "enviado", "EnviarDocumento", "Regenerate", "Regenerar",
        "StreamWriter", "File.WriteAll", "PdfCreator",
    ];

    /// <summary>
    /// The Peso PDF execution vocabulary the documents slice ADDED (sanctioned ONLY in the
    /// documents surface: <c>src/DMO.Application/Documents</c>, <c>DocumentsEndpoints.cs</c>, the
    /// composition-root wiring in <c>Program.cs</c> and the page-owned action hooks on the Approve
    /// page/adapter). It must stay out of every other P2-T06 source (BND2).
    /// </summary>
    public static IReadOnlyList<string> PesoPdfExecutionTokens { get; } =
    [
        "PesoPdf", "peso-pdf", "CanGeneratePesoPdf",
    ];

    /// <summary>No P2-T07 Boquilhas tokens (BND-B3).</summary>
    public static IReadOnlyList<string> BoquilhasTokens { get; } =
    [
        "Boquilha", "boquilha", "Movement", "movement_id", "RepairerResolution",
        "Início", "Irreparável", "BqAggregate",
    ];

    /// <summary>No Definições/settings tokens (BND-B1): every setting surface belongs to Controlo_Create.</summary>
    public static IReadOnlyList<string> SettingsTokens { get; } =
    [
        "Definições", "Definicoes", "Repairer", "repairer", "MachineAssignment",
        "PdfDirectory", "EmailList", "EmailTemplate", "GlassDensitySetting",
        "machine_assignment", "pdf_directory", "email_list", "email_template",
    ];

    /// <summary>No availability/navigation registration tokens (BND-B4).</summary>
    public static IReadOnlyList<string> AvailabilityTokens { get; } =
    [
        "CurrentBuildAvailable", "DestinationRouteRegistrations", "RegisterDestination",
        "RegisterRoute",
    ];

    /// <summary>No HISTÓRICO GLOBAL tokens (BND-B5/H4).</summary>
    public static IReadOnlyList<string> HistoriaTokens { get; } =
    [
        "historia", "HISTÓRICO GLOBAL", "HistoricoGlobal",
    ];

    /// <summary>No deferred-carrier invention tokens (BND-B6/CP2/D6): per-CM/Folha/Comparação/
    /// send persistence must not exist. (The per-CM VOCABULARY is not a carrier — it lives only in
    /// the documented constants type <c>PerCmDecisionVocabulary</c>, pinned by D3.)</summary>
    public static IReadOnlyList<string> DeferredCarrierTokens { get; } =
    [
        "previous_peso_id", "PreviousPesoId", "controlo_sheet_id", "ControloSheetId",
        "comparison", "Comparison",
        "per_cm", "PerCm", "cm_decision",
    ];

    /// <summary>No second calculation engine tokens (BND-B7).</summary>
    public static IReadOnlyList<string> CalculationEngineTokens { get; } =
    [
        "Calculate", "Capacity", "GlassWeight", "WaterDensity", "GlassDensity",
        "divisor", "Formula", "Resolve", "AwayFromZero",
    ];

    /// <summary>No generic lifecycle/revision/queue tokens (BND-B8).</summary>
    public static IReadOnlyList<string> GenericInfrastructureTokens { get; } =
    [
        "revision_id", "RevisionId", "StateMachine", "QueueTable", "AuditEngine",
        "GenericAudit",
    ];

    /// <summary>No legacy/fake identity tokens (I4/BND-B9).</summary>
    public static IReadOnlyList<string> LegacyIdentityTokens { get; } =
    [
        "production_id", "job_on_revision_id", "approval_peso_id", "ApprovalPeso",
        "ReviewCopy", "review_copy",
    ];

    /// <summary>The P2-T06-owned vocabularies that MUST exist (positive pin, D3).</summary>
    public static IReadOnlyList<string> OwnedDecisionTokens { get; } =
    [
        "aprovado", "nao_aprovado", "reaberto",
    ];

    /// <summary>The exact per-CM vocabulary pinned by the contract (D3) — must appear ONLY in
    /// contract/consultation form, never as persistence.</summary>
    public static IReadOnlyList<string> PerCmVocabularyTokens { get; } =
    [
        "Manter", "Colocar de parte",
    ];

    /// <summary>
    /// The protected persistence foundation pinned by normalized content hash (contract App. A):
    /// <c>DmoDbContext.cs</c> and migrations 001–005 (incl. Designers) are never edited; the
    /// model SNAPSHOT is the documented EF-generated extension and is NOT pinned.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PinnedProtectedFoundation { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/DMO.Infrastructure/Persistence/Core/DmoDbContext.cs"] = "32a1b78f1c393f3e19f27ebf0012c78c9ad96320de2afb4d1f33ae6aa844f2c5",
            ["src/DMO.Infrastructure/Migrations/20260922001736_AccountAndTemplateFoundation.cs"] = "4f5a8136547158fa6fe1b6a404d750e0a113a10c083359bdd1c0e901524c354f",
            ["src/DMO.Infrastructure/Migrations/20260922001736_AccountAndTemplateFoundation.Designer.cs"] = "6a68107f84beb8d893117eef38280ee0a33dd7fe3d6106208766f3c391c46f7a",
            ["src/DMO.Infrastructure/Migrations/20260922001757_TemplateModuleComposition.cs"] = "f9e17cc8fbd99077cc7c570cc80f9c16ea1d9acb7dbd6b3d7caaa3631f34b7fe",
            ["src/DMO.Infrastructure/Migrations/20260922001757_TemplateModuleComposition.Designer.cs"] = "0fba2f103fe768d32da82d4301ad02e5095cde2d18706424feb06ead19e85dc8",
            ["src/DMO.Infrastructure/Migrations/20260922232349_ToolJobOnDomainCore.cs"] = "4fb6a2d9a110830e26a100a4b1eb33a928cfee01d28b7c600e223b59dc6fef8d",
            ["src/DMO.Infrastructure/Migrations/20260922232349_ToolJobOnDomainCore.Designer.cs"] = "9a6cd0b2fd52683d357bef0ab7b12132d9892f4438625d6b43a6985b6cff79cc",
            ["src/DMO.Infrastructure/Migrations/20260923045054_ControloCreateDomain.cs"] = "d521309e5735219349c2d767debfe8de2eec1852a335f775b76ba831ac89e447",
            ["src/DMO.Infrastructure/Migrations/20260923045054_ControloCreateDomain.Designer.cs"] = "b3ae118b8f4525df46cc7b9629410e76e98a086e982a3dc9fb17e9b9f4bdfe8b",
            ["src/DMO.Infrastructure/Migrations/20260923122429_GlassDensitySettings.cs"] = "6dc136f9e9bac04eaf450f224b226b3a055f58e50f6c5196af1fb06f951e4688",
            ["src/DMO.Infrastructure/Migrations/20260923122429_GlassDensitySettings.Designer.cs"] = "b50480834ee8e4fc1c4b2363da35485a257a1d8909d68c479ded601e32e117d8",
        };

    /// <summary>The protected P2-T05 files of App. A (byte-identity pinned, not all listed: the
    /// P2-T05 surface files are covered by the P2-T05 regression hashes).</summary>
    public static IReadOnlyList<string> ProtectedP2T05TamperingProbe { get; } =
    [
        "src/DMO.Web/Pages/Controlo/ControloPolicyNames.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/PesoEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/PesoRepository.cs",
    ];
}