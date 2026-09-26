using DMO.IntegrationTests.JobOn;

namespace DMO.IntegrationTests.ControloCreate;

/// <summary>
/// P2-T05 boundary/regression scan helper: the P2-T05 production path tables and the
/// ownership/vocabulary tables used by the boundary rows BND1–BND9, PID5, REP4, MAC6, SNA5, AUT5
/// and LAY1.
/// </summary>
/// <remarks>
/// <para>
/// Authority: P2-T05 contract §17.5 (index tables), §25 (migration contract), §26.4 rows
/// PID5/MAC6/REP4/SNA5/AUT5/BND1–BND9/LAY1, §30 AC-P1/AC-P3/AC-D4/AC-E4/AC-H3/AC-G1/AC-Y1–AC-Y7/
/// AC-N1 and Appendix A (protected boundaries). Repository access and the comment-aware scanning
/// machinery are reused from the accepted <see cref="P2T04ProductionScan"/> (same assembly).</para>
/// <para>
/// The forbidden-vocabulary tables are composed from parts, exactly like the accepted P2-T04
/// helper, so this assertion source never carries a composed literal of its own. This helper
/// carries no test and asserts nothing: the rows live in <c>P2T05RegressionTests</c>.</para>
/// </remarks>
internal static class P2T05ProductionScan
{
    // ================= P2-T05 production sources ==========================================

    /// <summary>The new P2-T05 domain sources (contract Appendix B).</summary>
    public static IReadOnlyList<string> DomainSourcePaths { get; } =
        P2T04ProductionScan.FilesUnder("src/DMO.Domain/Controlo", ".cs");

    /// <summary>The new P2-T05 application sources (contract Appendix B; post-closure correction
    /// adds the glass-density settings repository contract).</summary>
    public static IReadOnlyList<string> ApplicationSourcePaths { get; } =
        P2T04ProductionScan.FilesUnder("src/DMO.Application/Controlo/Pesos", ".cs")
            .Concat(P2T04ProductionScan.FilesUnder("src/DMO.Application/Controlo/Settings", ".cs"))
            .Concat(
            [
                "src/DMO.Application/Repositories/IPesoRepository.cs",
                "src/DMO.Application/Repositories/IRepairerRepository.cs",
                "src/DMO.Application/Repositories/IMachineRepairerAssignmentRepository.cs",
                "src/DMO.Application/Repositories/IPdfDirectorySettingsRepository.cs",
                "src/DMO.Application/Repositories/IEmailListRepository.cs",
                "src/DMO.Application/Repositories/IEmailTemplateRepository.cs",
                "src/DMO.Application/Repositories/IGlassDensitySettingsRepository.cs",
                "src/DMO.Application/Persistence/ControloPersistenceException.cs",
            ])
            .ToList();

    /// <summary>The new P2-T05 persistence sources (contract Appendix B; post-closure correction
    /// adds the glass-density settings repository, entity and configuration).</summary>
    public static IReadOnlyList<string> PersistenceSourcePaths { get; } =
    [
        "src/DMO.Infrastructure/Persistence/Controlo/PesoRepository.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/RepairerRepository.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/MachineRepairerAssignmentRepository.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/PdfDirectorySettingsRepository.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EmailListRepository.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EmailTemplateRepository.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/GlassDensitySettingsRepository.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/DmoPesoContextRead.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/PesoJobOnDependencyProbe.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/PesoEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/PesoMeasurementRowEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/RepairerEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/MachineRepairerAssignmentEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/PdfDirectorySettingsEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/EmailListEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/EmailListRecipientEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/EmailTemplateEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/GlassDensitySettingEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/PesoEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/PesoMeasurementRowEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/RepairerEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/MachineRepairerAssignmentEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/PdfDirectorySettingsEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/EmailListEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/EmailListRecipientEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/EmailTemplateEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/GlassDensitySettingEntityConfiguration.cs",
        "src/DMO.Infrastructure/Configuration/ConfigurationCalculationConfiguration.cs",
    ];

    /// <summary>The P2-T05 migrations and their EF designers (contract §25.1; the post-closure
    /// correction adds its single additive migration 005).</summary>
    public static IReadOnlyList<string> MigrationSourcePaths { get; } =
    [
        "src/DMO.Infrastructure/Migrations/20260923045054_ControloCreateDomain.cs",
        "src/DMO.Infrastructure/Migrations/20260923045054_ControloCreateDomain.Designer.cs",
        "src/DMO.Infrastructure/Migrations/20260923122429_GlassDensitySettings.cs",
        "src/DMO.Infrastructure/Migrations/20260923122429_GlassDensitySettings.Designer.cs",
    ];

    /// <summary>
    /// The table names the P2-T05 migration actually creates, read from its own
    /// <c>CreateTable(name: "…")</c> calls, in ordinal order. Exactly the eight contracted tables.
    /// </summary>
    public static IReadOnlyList<string> CreatedTableNames() =>
        System.Text.RegularExpressions.Regex.Matches(
                P2T04ProductionScan.Read(MigrationSourcePaths[0]),
                @"CreateTable\(\s*name:\s*""(?<name>[A-Za-z0-9_]+)""")
            .Select(match => match.Groups["name"].Value)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>The new P2-T05 Web sources (contract Appendix B).</summary>
    public static IReadOnlyList<string> WebSourcePaths { get; } =
        P2T04ProductionScan.FilesUnder("src/DMO.Web/Pages/Controlo", ".cs", ".cshtml")
            .Concat(
            [
                "src/DMO.Web/Endpoints/ControloCreateEndpoints.cs",
                "src/DMO.Web/Endpoints/ControloDefinicoesEndpoints.cs",
            ])
            .ToList();

    /// <summary>The new P2-T05 assets (contract §23.3 rule 1).</summary>
    public static IReadOnlyList<string> AssetPaths { get; } =
    [
        "src/DMO.Web/wwwroot/css/dmo-controlo.css",
        "src/DMO.Web/wwwroot/js/dmo-controlo.js",
    ];

    /// <summary>Every P2-T05 production source scanned by the vocabulary rows.</summary>
    public static IReadOnlyList<string> ProductionSourcePaths { get; } =
        DomainSourcePaths
            .Concat(ApplicationSourcePaths)
            .Concat(PersistenceSourcePaths)
            .Concat(MigrationSourcePaths)
            .Concat(WebSourcePaths)
            .Concat(AssetPaths)
            .ToList();

    // ================= forbidden vocabulary (composed from parts) ========================

    /// <summary>
    /// The legacy/fake production-identity tokens (contract §3.4, AC-P1/AC-P3, PID5). Composed from
    /// parts so this assertion source never carries the literal.
    /// </summary>
    public static IReadOnlyList<string> LegacyIdentityTokens { get; } =
    [
        string.Concat("production", "_id"),
        string.Concat("job", "_on_revision", "_id"),
        string.Concat("production", "Id"),
        string.Concat("jobOn", "Revision", "Id"),
        string.Concat("revision", "Id"),
        string.Concat("rascunho"),
    ];

    /// <summary>
    /// The <b>quoted schema names</b> that would declare a machine registry, a machines table or a
    /// history table (BND6/AC-E4, MAC6). The quoted form is deliberate: the legitimate
    /// <c>"machine_repairer_assignments"</c> table and its <c>"machine"</c> column can never satisfy
    /// a quoted-name match of <c>"machines"</c>/<c>"machine_id"</c>.
    /// </summary>
    public static IReadOnlyList<string> QuotedSchemaTokens { get; } =
    [
        string.Concat("\"", "machine", "s", "\""),
        string.Concat("\"", "machine", "_id", "\""),
        string.Concat("\"", "machine", "_registry", "\""),
        string.Concat("\"", "assignment", "_history", "\""),
        string.Concat("\"", "machine", "s_", "history", "\""),
    ];

    /// <summary>
    /// The P2-T06 leakage vocabulary (BND2/AC-Y2): approval workflow members, rewrite paths and
    /// queue/decision persistence. The legitimate three-value status tokens
    /// (<c>aprovado</c>/<c>nao_aprovado</c>) and their domain enum members are deliberately absent.
    /// </summary>
    public static IReadOnlyList<string> ApprovalLeakageTokens { get; } =
    [
        string.Concat("approve", "Async"),
        string.Concat("reject", "Async"),
        string.Concat("reopen", "Async"),
        string.Concat("Approve", "Peso"),
        string.Concat("Pending", "Review"),
        string.Concat("approval", "_queue"),
        string.Concat("decision", "_table"),
        string.Concat("Mant", "er"),
        string.Concat("Colocar", "DeParte"),
        string.Concat("/approve"),
    ];

    /// <summary>
    /// The P2-T07 leakage vocabulary (BND3/AC-Y3): Boquilhas aggregates/movements, resolution
    /// machinery and the machine sidebar. The legitimate repairer register vocabulary is NOT
    /// scanned. The <c>boquilhas</c> identity exists only in the protected <c>ModuleCatalog</c>.
    /// </summary>
    public static IReadOnlyList<string> BoquilhasLeakageTokens { get; } =
    [
        string.Concat("boqui", "lhas_id"),
        string.Concat("movement", "_type"),
        string.Concat("Move", "mentEntity"),
        string.Concat("In", "\u00edcio"),
        string.Concat("Irrepar", "\u00e1vel"),
        string.Concat("Resolve", "Repairer"),
        string.Concat("machine", "_sidebar"),
    ];

    /// <summary>
    /// The P2-T08 leakage vocabulary (BND4/AC-Y4): PDF/file generation, email transport,
    /// availability reads, routing rules and placeholder parsing. The legitimate settings
    /// vocabulary (directory CHECK, lists, templates) is NOT scanned; the directory probe's own
    /// probe-file IO is the contracted exception (§12.2 "no file IO beyond the check probe itself").
    /// </summary>
    public static IReadOnlyList<string> DocumentLeakageTokens { get; } =
    [
        string.Concat("Pdf", "Generator"),
        string.Concat("Generate", "Pdf"),
        string.Concat("Smtp"),
        string.Concat("SendGrid"),
        string.Concat("MailKit"),
        string.Concat("email", "_send"),
        string.Concat("SendAsync"),
        string.Concat("Availabilit", "yState"),
        string.Concat("file", "_missing"),
        string.Concat("placeholder", "parse"),
        string.Concat("routing", "_rule"),
        string.Concat("Attachment", "Path"),
    ];

    /// <summary>
    /// The Comparação/Pegamentos/Folha/Resumo identity tokens (BND7/AC-Y5, Q-SCOPE): no
    /// <c>previous_peso_id</c> relation, no record tables and no route exists. The legitimate
    /// email-template <c>document_type</c> CHECK values (<c>pegamentos</c>/<c>resumo</c>) are
    /// contracted (§14.2) and are deliberately NOT scanned as values — only their identity forms
    /// (tables/columns/types/routes) are.
    /// </summary>
    public static IReadOnlyList<string> ScopeBoundaryTokens { get; } =
    [
        string.Concat("previous", "_peso", "_id"),
        string.Concat("PreviousPeso", "Id"),
        string.Concat("compara", "\u00e7\u00e3o"),
        string.Concat("controlo", "_sheet"),
        string.Concat("controle", "_sheet"),
        string.Concat("ControloS", "heet"),
        string.Concat("resumo", "_id"),
        string.Concat("resumos"),
        string.Concat("pegamentos", "_id"),
        string.Concat("Pegamentos", "Entity"),
        string.Concat("Resumo", "Entity"),
        string.Concat("Compara", "Entity"),
    ];

    /// <summary>
    /// The ONE P2-T05 owned path disclosed from the BND7 scope-boundary token scan: the Resumo
    /// landing page itself (<c>Resumo.cshtml.cs</c>) legitimately renders the accepted plural
    /// display title <c>Resumos desta referência</c> (the production-switcher heading of the owned
    /// Resumo surface, P2-T05 UI). That display text collides by shape with the plural identity
    /// token <c>resumos</c>, but it is user-visible text, not a table/column/type/route identity —
    /// the BND7 identity scan must not flag the owned page for its own accepted heading. Only this
    /// single owned display surface is disclosed and the row asserts the disclosure is never
    /// vacuous (the file exists and really carries the accepted title); the forbidden vocabulary
    /// stays untouched for every other path. This mirrors the P2-T04-side pairing, whose BND7 row
    /// discloses its own later-stream additive files the same way (P2-T04 BND7).
    /// </summary>
    public const string DisclosedResumoSurfaceDisplayPath =
        "src/DMO.Web/Pages/Controlo/Resumo.cshtml.cs";

    /// <summary>
    /// The snapshot-engine vocabulary (SNA5/AC-H3): no generic snapshot table/engine and no copy of
    /// live Tool values into Peso rows exists.
    /// </summary>
    public static IReadOnlyList<string> SnapshotEngineTokens { get; } =
    [
        string.Concat("snapshot", "_table"),
        string.Concat("Snapshot", "Engine"),
        string.Concat("jsonb"),
        string.Concat("Tool", "Snapshot"),
    ];

    /// <summary>
    /// The invented-repairer-field tokens (REP4/AC-D4): no address/email/phone/supplier/tax/
    /// contact-person field exists in the P2-T05 code or schema. The legitimate email-list
    /// <c>address</c> vocabulary is NOT scanned; only repairer-scoped field declarations are found
    /// through the member scan of <c>Repairer</c>/<c>RepairerEntity</c>.
    /// </summary>
    public static IReadOnlyList<string> RepairerFieldTokens { get; } =
    [
        string.Concat("Address"),
        string.Concat("Phone"),
        string.Concat("Nif"),
        string.Concat("Vat"),
        string.Concat("Fiscal"),
        string.Concat("Supplier"),
        string.Concat("Contact"),
        string.Concat("Email"),
    ];

    /// <summary>The line-grouping tokens of MAC6/AC-E4 (the machine CODE column and the
    /// <c>machine_repairer_assignments</c> table are legitimate; no group semantics exist).</summary>
    public static IReadOnlyList<string> LineGroupingTokens { get; } =
    [
        string.Concat("linha", "_b"),
        string.Concat("linha", "_c"),
        string.Concat("line", "_group"),
        string.Concat("Group", "Code"),
    ];

    // ================= ownership / allow-list ============================================

    /// <summary>
    /// The disclosed outputs-slice Job On files of the changed-path allow-list (BND9/AC-Y7): the
    /// Job On owned files that NECESSARILY carry the Peso output vocabulary of what they expose —
    /// the <c>jobon_id → peso_id</c> read projection and service, the controlled <c>peso-pdf</c>
    /// open route and the sheet outputs section. They are spread into <see cref="OwnedPathPrefixes"/>
    /// (the same files are disclosed for the P2-T04-side scan by the P2-T04 BND7 row), and BND9
    /// asserts each disclosed file really carries the additive outputs vocabulary (never vacuous).
    /// </summary>
    public static IReadOnlyList<string> DisclosedOutputsSliceSourcePaths { get; } =
    [
        "src/DMO.Application/JobOn/IJobOnControlOutputsService.cs",
        "src/DMO.Application/JobOn/JobOnControlOutputsModels.cs",
        "src/DMO.Application/JobOn/JobOnControlOutputsService.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/DmoPesoOutputRead.cs",
        "src/DMO.Web/Endpoints/JobOnEndpoints.cs",
        "src/DMO.Web/Pages/JobOn/View.cshtml",
        "src/DMO.Web/Pages/JobOn/View.cshtml.cs",
    ];

    /// <summary>The P2-T05 owned source path prefixes (contract Appendix B and §23; the
    /// post-closure correction adds the glass-density settings sources and migration 005).</summary>
    public static IReadOnlyList<string> OwnedPathPrefixes { get; } =
    [
        "src/DMO.Domain/Controlo/",
        "src/DMO.Application/Controlo/Pesos/",
        "src/DMO.Application/Controlo/Settings/",
        "src/DMO.Application/Repositories/I",
        "src/DMO.Application/Persistence/Controlo",
        "src/DMO.Infrastructure/Persistence/Controlo/Peso",
        "src/DMO.Infrastructure/Persistence/Controlo/Repairer",
        "src/DMO.Infrastructure/Persistence/Controlo/MachineRepairerAssignment",
        "src/DMO.Infrastructure/Persistence/Controlo/PdfDirectorySettings",
        "src/DMO.Infrastructure/Persistence/Controlo/EmailList",
        "src/DMO.Infrastructure/Persistence/Controlo/EmailTemplate",
        "src/DMO.Infrastructure/Persistence/Controlo/GlassDensity",
        "src/DMO.Infrastructure/Persistence/Controlo/DmoPesoContextRead.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/PesoJobOnDependencyProbe.cs",
        // The Resumo read seam consumes the shared Controlo.Pesos namespace in its using, so it
        // now carries the "peso" fragment in code; it is D5-owned and disclosed here with the
        // other read seams (same convention as DmoPesoContextRead/DmoPesoOutputRead).
        "src/DMO.Infrastructure/Persistence/Controlo/DmoProductionResumoRead.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/Peso",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/PesoMeasurementRowEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/RepairerEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/MachineRepairerAssignmentEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/PdfDirectorySettingsEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/EmailList",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/EmailTemplateEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/GlassDensity",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/Peso",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/PesoMeasurementRowEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/RepairerEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/MachineRepairerAssignmentEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/PdfDirectorySettingsEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/EmailList",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/EmailTemplateEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/GlassDensity",
        "src/DMO.Infrastructure/Configuration/ConfigurationCalculationConfiguration.cs",
        "src/DMO.Infrastructure/Migrations/20260923045054_ControloCreateDomain",
        "src/DMO.Infrastructure/Migrations/20260923122429_GlassDensitySettings",
        "src/DMO.Infrastructure/Migrations/20260924182527_EmailTemplateGroupRouting",
        "src/DMO.Web/Pages/Controlo/",
        "src/DMO.Web/Endpoints/Controlo",
        "src/DMO.Web/wwwroot/css/dmo-controlo.css",
        "src/DMO.Web/wwwroot/js/dmo-controlo.js",
        // ---- P2-T06 (disclosed extension, P2-T06 contract Appendix B): the Controlo Approve
        // surface consumes the same Peso identity/read model by contract, so its OWN paths are an
        // accepted extension of the P2-T05 owned surface (the P2-T06 paths are themselves pinned by
        // the P2-T06 boundary rows).
        "src/DMO.Application/Controlo/Approve/",
        "src/DMO.Application/Repositories/IPesoReview",
        "src/DMO.Application/Persistence/PesoReviewPersistenceException.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/PesoReview",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/PesoReviewDecisionEntity.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/PesoReviewDecisionEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/PesoReviewableIndexConfiguration.cs",
        "src/DMO.Infrastructure/Migrations/20260923171223_ControloApproveDomain",
        "src/DMO.Web/Pages/Controlo/Approve/",
        "src/DMO.Web/Endpoints/ControloApproveEndpoints.cs",
        "src/DMO.Web/wwwroot/css/dmo-controlo-approve.css",
        "src/DMO.Web/wwwroot/js/dmo-controlo-approve.js",
        // ---- P2-T07 (disclosed extension, P2-T07 Appendix B + OWNER CLARIFICATION): the Boquilhas
        // surface consumes the P2-T05 repairer register and machine assignments by contract
        // (read-only), so its OWN paths are an accepted extension of the P2-T05 owned surface (the
        // P2-T07 paths are themselves pinned by the P2-T07 boundary rows; the migration 007 pair
        // was corrected pre-closure to the final register schema). §34 (disclosed extension of the
        // same OWNER clarification, P2-T05 §31.3): the repairer FAMILY surface moved to
        // Boquilhas > Definições (same physical tables; ownership/service/UI only), so the
        // Boquilhas Definições endpoint file and the delta migration 008 (whose Designer mirrors
        // the full model, incl. the P2-T05 tables) are accepted extensions too.
        "src/DMO.Domain/Boquilhas/",
        "src/DMO.Application/Boquilhas/",
        "src/DMO.Application/Repositories/IBoquilhas",
        "src/DMO.Application/Persistence/BoquilhasPersistenceException.cs",
        "src/DMO.Infrastructure/Persistence/Boquilhas/Boquilhas",
        "src/DMO.Infrastructure/Persistence/Boquilhas/Entities/Boquilha",
        "src/DMO.Infrastructure/Persistence/Boquilhas/EntityConfigurations/Boquilha",
        "src/DMO.Infrastructure/Migrations/20260924051151_BoquilhasDomain",
        "src/DMO.Infrastructure/Migrations/20260924130151_BoquilhasPreJobonAssociation",
        "src/DMO.Web/Pages/Boquilhas/",
        "src/DMO.Web/Endpoints/BoquilhasEndpoints.cs",
        "src/DMO.Web/Endpoints/BoquilhasDefinicoesEndpoints.cs",
        "src/DMO.Web/wwwroot/css/dmo-boquilhas.css",
        "src/DMO.Web/wwwroot/js/dmo-boquilhas.js",
        // ---- P2-T08 documents slice (disclosed extension): the Documents area consumes the P2-T05
        // Peso read model and the operator-configured pdf_directory_settings by contract and owns
        // generation/storage, so its OWN paths are an accepted extension of the P2-T05 owned
        // surface (the documents slice pins its own boundary/regression rows).
        "src/DMO.Application/Documents/",
        "src/DMO.Web/Endpoints/DocumentsEndpoints.cs",
        // ---- Peso Comparação (disclosed extension, this slice): the Comparação aggregate
        // consumes the SHARED Peso read, the cm-context traversal read and the Peso calculation
        // path by contract and owns its persistence/application core, so its OWN paths are an
        // accepted extension of the P2-T05 owned surface (the Comparação surface pins its own
        // boundary/regression rows).
        "src/DMO.Domain/ControloComparacao/",
        "src/DMO.Application/Controlo/Comparacao/",
        "src/DMO.Application/Repositories/IComparacao",
        "src/DMO.Application/Persistence/Comparacao",
        "src/DMO.Infrastructure/Persistence/Controlo/Comparacao",
        "src/DMO.Infrastructure/Persistence/Controlo/Entities/Comparacao",
        "src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations/Comparacao",
        "src/DMO.Infrastructure/Migrations/20260925071139_ControloComparacaoDomain",
        // ---- P2-T08 outputs slice (disclosed extension, BND7-paired): the Job On sheet exposes
        // the Peso outputs of the occurrence (the jobon_id → peso_id read projection and the
        // controlled peso-pdf open route), so the Job On owned files of the slice necessarily
        // carry the Peso vocabulary of what they expose. Disclosed here exactly like the P2-T04
        // BND7 row discloses the same files; BND9 asserts each carries the outputs vocabulary.
        ..DisclosedOutputsSliceSourcePaths,
    ];

    /// <summary>
    /// The only accepted non-new files P2-T05 changes (contract Appendix A, last paragraph, and
    /// §20.4.2): the additive DI registrations, the EF-generated snapshot extension and the three
    /// Job On application-contract files of the sanctioned Q-CAND additive member.
    /// </summary>
    public static IReadOnlyList<string> DocumentedAdditiveSourcePaths { get; } =
    [
        "src/DMO.Web/Program.cs",
        "src/DMO.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs",
        "src/DMO.Infrastructure/Migrations/DmoDbContextModelSnapshot.cs",
        "src/DMO.Application/JobOn/IJobOnService.cs",
        "src/DMO.Application/JobOn/JobOnService.cs",
        "src/DMO.Application/JobOn/JobOnModels.cs",
        "src/DMO.Domain/Controlo/PesoReviewDecisionId.cs",
        "src/DMO.Domain/Controlo/PesoReviewDecisionKind.cs",
        "src/DMO.Domain/Controlo/PesoReviewDecision.cs",
    ];

    /// <summary>The P2-T05 vocabulary fragments of the changed-path allow-list row (BND9; the
    /// post-closure correction adds the glass-density vocabulary).</summary>
    public static IReadOnlyList<string> SourceVocabularyFragments { get; } =
    [
        string.Concat("peso"),
        string.Concat("repair", "er"),
        string.Concat("Defini", "\u00e7\u00f5es"),
        string.Concat("Defini", "coes"),
        string.Concat("email", "_list"),
        string.Concat("email", "_template"),
        string.Concat("pdf", "_directory"),
        string.Concat("machine", "_repairer"),
        string.Concat("glass", "_density"),
        string.Concat("Glass", "Density"),
    ];

    /// <summary>Whether the supplied relative path belongs to the P2-T05 owned surface.</summary>
    public static bool IsOwnedPath(string relativePath) =>
        OwnedPathPrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.Ordinal))
        || DocumentedAdditiveSourcePaths.Contains(relativePath, StringComparer.Ordinal);

    /// <summary>
    /// Every <c>src</c> file that mentions the P2-T05 vocabulary in CODE/markup outside the owned
    /// paths and the documented additive files (BND9/AC-Y7). The scan is comment-aware (the
    /// accepted <see cref="P2T04ProductionScan"/> convention): a documentation remark about an
    /// excluded concept is not a behaviour mention.
    /// </summary>
    public static IReadOnlyList<string> VocabularyMentionsOutsideOwnedPaths()
    {
        var offenders = new List<string>();

        foreach (var path in P2T04ProductionScan.FilesUnder(
                     "src", ".cs", ".cshtml", ".css", ".js", ".json"))
        {
            var content = P2T04ProductionScan.Read(path);

            var mentions = SourceVocabularyFragments.Any(fragment =>
                P2T04ProductionScan.SubstringOccurrences(content, fragment).Count > 0);

            if (!mentions || IsOwnedPath(path))
            {
                continue;
            }

            offenders.Add(path);
        }

        return offenders;
    }
}