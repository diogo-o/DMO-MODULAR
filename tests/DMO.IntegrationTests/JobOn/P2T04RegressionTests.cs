using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using DMO.Application.Access;
using DMO.Application.Session;
using DMO.Domain.Tools;
using DMO.Infrastructure.Persistence.Entities;
using DMO.IntegrationTests.Frontend.Shared;
using DMO.IntegrationTests.Host;
using DMO.Web.Authorization;
using DMO.Web.Frontend.Shell;
using DMO.Web.Navigation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DMO.IntegrationTests.JobOn;

/// <summary>
/// P2-T04 (B/C) integration — the boundary/regression block of the test matrix: the interim runtime
/// state, the routing boundary, the protected-file byte-identity, the excluded vocabulary and the
/// changed-path allow-list.
/// </summary>
/// <remarks>
/// <para>
/// Authority: <c>plans/contracts/P2-T04_DOMAIN_CORE_TOOL_JOBON_CONTRACT.md</c> §13.5 (interim runtime
/// state), §13.6 (routing boundary), §16 (migration contract), §17 (explicit non-scope), §17.2
/// (protected files), §18 (P2-T03 composition), §20.6 rows RTE11/RTE12/RTE14/RTE15, §20.8 rows
/// BND1–BND14 and AC-91 … AC-106.
/// </para>
/// <para>
/// Purpose: prove P2-T04 is structurally additive — no availability entry, no destination route, no
/// navigation entry, no new policy, no protected artifact edited, no excluded identity/repairer/
/// machine-registry/P2-T05+ concept present, and no path changed outside the contracted allow-list.
/// Preconditions: the real compiled host, the accepted pinned artifacts and the P2-T04 test
/// composition.
/// Required non-effects: none; this class asserts non-effects and changes nothing.
/// </para>
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class P2T04RegressionTests
{
    /// <summary>
    /// The protected persistence foundation pinned by normalized content hash (contract §16.5 and
    /// AC-96): <c>DmoDbContext.cs</c> is deliberately not modified, and migrations 001/002 are never
    /// edited. A change here is a contract amendment, not a P2-T04 edit.
    /// </summary>
    private static readonly Dictionary<string, string> PinnedProtectedFoundation = new(StringComparer.Ordinal)
    {
        ["src/DMO.Infrastructure/Persistence/Core/DmoDbContext.cs"] = "32a1b78f1c393f3e19f27ebf0012c78c9ad96320de2afb4d1f33ae6aa844f2c5",
        ["src/DMO.Infrastructure/Migrations/20260922001736_AccountAndTemplateFoundation.cs"] = "4f5a8136547158fa6fe1b6a404d750e0a113a10c083359bdd1c0e901524c354f",
        ["src/DMO.Infrastructure/Migrations/20260922001736_AccountAndTemplateFoundation.Designer.cs"] = "6a68107f84beb8d893117eef38280ee0a33dd7fe3d6106208766f3c391c46f7a",
        ["src/DMO.Infrastructure/Migrations/20260922001757_TemplateModuleComposition.cs"] = "f9e17cc8fbd99077cc7c570cc80f9c16ea1d9acb7dbd6b3d7caaa3631f34b7fe",
        ["src/DMO.Infrastructure/Migrations/20260922001757_TemplateModuleComposition.Designer.cs"] = "0fba2f103fe768d32da82d4301ad02e5095cde2d18706424feb06ead19e85dc8",
    };

    /// <summary>
    /// The access/authorization foundation pinned by normalized content hash (contract §17.2 and
    /// AC-101): the canonical 13-Module catalog and the deterministic policy projection are
    /// byte-identical, so no Module and no policy was added.
    /// </summary>
    private static readonly Dictionary<string, string> PinnedAccessFoundation = new(StringComparer.Ordinal)
    {
        ["src/DMO.Application/Access/ModuleCatalog.cs"] = "7f6395d1b9af43fec3daad7e597de205934e438968c1ad3e4f811451c45c909d",
        ["src/DMO.Web/Authorization/ModuleAuthorizationPolicies.cs"] = "d34f0bbac398b343719d18cb74ea98509bbbd59ff84f33fdff44a53652fe4b76",
    };

    /// <summary>
    /// Every P2-T01/P2-T02/P2-T03 contract type, shared partial, stylesheet, script and the P2-T03
    /// presentation fixture, pinned by normalized content hash (contract §17.2, §18.2 rule 1 and
    /// AC-97). The P2-T01 subset is <i>additionally</i> validated through the accepted
    /// <c>P2T02RegressionTests.FrozenP2T01Artifacts</c> pins, read by reflection exactly as the
    /// accepted <c>P2T03RegressionTests.RG3</c> does.
    /// </summary>
    private static readonly Dictionary<string, string> PinnedSharedFrontendArtifacts = new(StringComparer.Ordinal)
    {
        ["src/DMO.Web/Frontend/Shared/Contracts/AuditEntryPresentation.cs"] = "3391a4adbb4b23506ed8cb433fdc104cf4ef6106adf3baf5ac204558577488db",
        ["src/DMO.Web/Frontend/Shared/Contracts/AuditTrailPresentation.cs"] = "a9325cc69d2e9d14c74b409fa6d35d0773d1a06d86ecc1be0eae834f50074fc7",
        ["src/DMO.Web/Frontend/Shared/Contracts/AvailabilityPresentation.cs"] = "2979bd8b7595492ba6a893fee83247318cfccf8b4dd8420717b1b44004c06a91",
        ["src/DMO.Web/Frontend/Shared/Contracts/AvailabilityState.cs"] = "e6de1bc28d1a794a2259f14756111ebbf1af47e8650d5de9829d45bced187a41",
        ["src/DMO.Web/Frontend/Shared/Contracts/AvailabilityTraits.cs"] = "430fc25c70b3ad9dc7b42819881ded4f8dad32b153809bc366c4552bb250b5f1",
        ["src/DMO.Web/Frontend/Shared/Contracts/AvailabilityVersionPresentation.cs"] = "de2e685dfd12b4095cc605112bebb1384c9041deff4be8011279848da90e6f71",
        ["src/DMO.Web/Frontend/Shared/Contracts/CommonState.cs"] = "6fcbb6c5722e55110cb3e24998d542da210e30faf92f7379dbf03ee2a4edca00",
        ["src/DMO.Web/Frontend/Shared/Contracts/CommonStateRegionPresentation.cs"] = "7eb4c14d884b6d7e26ccd524d4d601f10800aa0eb413eec320735ae04ec244d5",
        ["src/DMO.Web/Frontend/Shared/Contracts/CommonStateTraits.cs"] = "734aa5301d1058598749403b21255e7a66e6a4b5d244dc2cf04d4cab53b81ff7",
        ["src/DMO.Web/Frontend/Shared/Contracts/DecisionBarActionGroup.cs"] = "9f3d40153001d1139e810b4509c37b3a818cd6147fa9e906b24f56aa51977e4d",
        ["src/DMO.Web/Frontend/Shared/Contracts/DecisionBarActionPresentation.cs"] = "3d27699d7aa14cec5a665af7c1bdc7dcd27855f2b4b9dc0dd76e76b4ee79b367",
        ["src/DMO.Web/Frontend/Shared/Contracts/DecisionBarEvent.cs"] = "e944b29e5af20a83397ae4063bf8c757dbddc2e3e2df08b00292f485bb297316",
        ["src/DMO.Web/Frontend/Shared/Contracts/DecisionBarEventKind.cs"] = "505adf276aa8339c268dac9c1d9ac8a8a5a5fd83f8d18e44e1de2101e4903164",
        ["src/DMO.Web/Frontend/Shared/Contracts/DecisionBarInteraction.cs"] = "64ef1a36a4a31a29255f8d992c16f5b2e96f286ba218134757c947dc4943b23b",
        ["src/DMO.Web/Frontend/Shared/Contracts/DecisionBarOutcome.cs"] = "c7aae7f2a00b0b0df0553de6aea702280cd62f1354b7c990ad512e308acf12ce",
        ["src/DMO.Web/Frontend/Shared/Contracts/DecisionBarPresentation.cs"] = "6652dc914d285054aa6633d48b20ab55c5404c8e9a43cae692751d2be03cba69",
        ["src/DMO.Web/Frontend/Shared/Contracts/DenseTableCellPresentation.cs"] = "c4cd6afd881bef881bc8a8a02bb082574bdb22224fb6f483e852e96515f9ab56",
        ["src/DMO.Web/Frontend/Shared/Contracts/DenseTableColumnAlignment.cs"] = "35d3a8d6023930c2ca425290a635caf3f685e0f650451d256971de8701f8f7a9",
        ["src/DMO.Web/Frontend/Shared/Contracts/DenseTableColumnPresentation.cs"] = "57d2acccc98f16e35bdcace020cc1993f9cd3b1dafc7bb236e90e036d362a37b",
        ["src/DMO.Web/Frontend/Shared/Contracts/DenseTableColumnWidthHint.cs"] = "83edb5e47fb1b67d0d508661f2280d5d6f3a5fca0e797a4680247a80dc342b67",
        ["src/DMO.Web/Frontend/Shared/Contracts/DenseTableEvent.cs"] = "6754c4d83c1344bee77a056dc2be940d111b717f41873888002ac223ffe5a397",
        ["src/DMO.Web/Frontend/Shared/Contracts/DenseTableEventKind.cs"] = "47fc61385a15aa9e5121dac6635e1c56be2ec48ab7df1248e96a872c8fcd8d89",
        ["src/DMO.Web/Frontend/Shared/Contracts/DenseTableFilterOptionPresentation.cs"] = "b80bf15046d47c1f745b3fbb41272911a009f6515ce432b44751dded00854f03",
        ["src/DMO.Web/Frontend/Shared/Contracts/DenseTableFilterPresentation.cs"] = "a4c04971bb85f2a368a3e9aa7245f5e7e7d26b3677aab6e9021e239db44c3d5f",
        ["src/DMO.Web/Frontend/Shared/Contracts/DenseTableInteraction.cs"] = "9c5de831aedd398a061f742389fcd4f7207a263a9f2da3896a1aed60bd0eca93",
        ["src/DMO.Web/Frontend/Shared/Contracts/DenseTableOutcome.cs"] = "7b778d8a3973b42cf750633128e3782829ca31b66813edb44263c1a6901b8efb",
        ["src/DMO.Web/Frontend/Shared/Contracts/DenseTablePagingPresentation.cs"] = "f70dae81b2fa6d4b9b42a0f3183bac7906d71f82b550f0d1f4d74f9dfe18e24c",
        ["src/DMO.Web/Frontend/Shared/Contracts/DenseTablePresentation.cs"] = "891d39d5b82c7d3dbbae646f6a2181ca3a68dcaa466b2d46e9ff66c06dbd9ca3",
        ["src/DMO.Web/Frontend/Shared/Contracts/DenseTableRowPresentation.cs"] = "cf9633e9e3f6d735f5006fba900eacd41ed637932710e93939b387e29e63b391",
        ["src/DMO.Web/Frontend/Shared/Contracts/DenseTableSortDirection.cs"] = "f1a2b2679d0e1a50e757b1e5e072afd4926411a9fbd5c0b7f1577be704023296",
        ["src/DMO.Web/Frontend/Shared/Contracts/DenseTableSortPresentation.cs"] = "78934fc008340f76c9f014edffe13e2ccf1d0c7d58315aa911b3b61a7c4584c7",
        ["src/DMO.Web/Frontend/Shared/Contracts/MeasurementRowFieldKind.cs"] = "ad5112fe73a2d8b22d48de2f59f542a646d45b35af811ba652487a13c818f7ee",
        ["src/DMO.Web/Frontend/Shared/Contracts/MeasurementRowFieldOptionPresentation.cs"] = "8a590ccac0e958bad5000440168492f3f316027053a7738abd653c20a33672c7",
        ["src/DMO.Web/Frontend/Shared/Contracts/MeasurementRowFieldPresentation.cs"] = "2ab0b6579da629f119778e82ed891fd9fa47177de9a42b64cd5aeea090cb36e8",
        ["src/DMO.Web/Frontend/Shared/Contracts/MeasurementRowPresentation.cs"] = "a2f5c669ef22c94ec835ec56e285247a40a81d1f8155ba28d8871be1cb60b73b",
        ["src/DMO.Web/Frontend/Shared/Contracts/MeasurementRowsEvent.cs"] = "a64ae1b0566ae3af4fb1c541c49ff577f271ff107a6d7c8dfd0c6dc7a9b098cc",
        ["src/DMO.Web/Frontend/Shared/Contracts/MeasurementRowsEventKind.cs"] = "4d91e96c8535cf41c0d90de02def85229f6ed3cf75a11595d80658d75492eedf",
        ["src/DMO.Web/Frontend/Shared/Contracts/MeasurementRowsFocusKind.cs"] = "6e84cd9ed2fc765412a5a6ca54c16d7373d9f4b05b75e38ce6b6035e797fe877",
        ["src/DMO.Web/Frontend/Shared/Contracts/MeasurementRowsFocusTarget.cs"] = "2f7f19f67540f8e73393c62ba974209d40d104eecd8ddf64bab4bf52c74a048e",
        ["src/DMO.Web/Frontend/Shared/Contracts/MeasurementRowsInteraction.cs"] = "a82a1b81d8aa1d972744ff1d6be7afdbfb2a953770d88835468661bd0b83320c",
        ["src/DMO.Web/Frontend/Shared/Contracts/MeasurementRowsOutcome.cs"] = "76f1107a0d401524ba540873318cc02877a6d489ca9d303a07479dd9b80b955c",
        ["src/DMO.Web/Frontend/Shared/Contracts/MeasurementRowsPresentation.cs"] = "ca46ffa7daba57b184af44436c953446df1f3e40ecc889dde7bb2394c3bc20c6",
        ["src/DMO.Web/Frontend/Shared/Contracts/RecordStatusPresentation.cs"] = "7dd4cc06f7a8caba156c032b4552cb7102b330b2a9eee06c72aaa4d44313efb6",
        ["src/DMO.Web/Frontend/Shared/Contracts/SharedActionPresentation.cs"] = "92674f51ba8d9dc5d88ab83d2bd44a76669aa82ead99dec2113ccb3c7503e99f",
        ["src/DMO.Web/Frontend/Shared/Contracts/StatusTone.cs"] = "068322db5fac137dea7c7b4fd1da22a9988dec748d8831c9bc64a762b36a92b4",
        ["src/DMO.Web/Frontend/Shared/Contracts/ToolPickerCandidatePresentation.cs"] = "86550a8ea61d708924a61255a3f6208fd12a36da37649698000d0057722d4d04",
        ["src/DMO.Web/Frontend/Shared/Contracts/ToolPickerEvent.cs"] = "913c40134b9ed3a7418fa82f2886d0ee4ff3179363b109a72110afd04f9ccc6b",
        ["src/DMO.Web/Frontend/Shared/Contracts/ToolPickerEventKind.cs"] = "ded5bd7b1f7e818700eb3c1028d777005be7fbddaf59d28ab464544b52e35239",
        ["src/DMO.Web/Frontend/Shared/Contracts/ToolPickerFactPresentation.cs"] = "a72e36192ce5dbcbbefcc8c3422c658dccb10cdcade2d13864fdf20a320e9d57",
        ["src/DMO.Web/Frontend/Shared/Contracts/ToolPickerFocusTarget.cs"] = "dbbe2e092ff446566dddc19ae41501248ff5961454b6c963910082dd62ce6c54",
        ["src/DMO.Web/Frontend/Shared/Contracts/ToolPickerInteraction.cs"] = "eccd81014dd9b657c0c549de99c557c55b1b55521eb7e4b176db0ee3d0ea7a27",
        ["src/DMO.Web/Frontend/Shared/Contracts/ToolPickerOutcome.cs"] = "3ef77f4450060b9574b145d1242b6a912c7c055e34b14afcafe466acef22adce",
        ["src/DMO.Web/Frontend/Shared/Contracts/ToolPickerPresentation.cs"] = "70cfcafd8901cda6930ad54b94cf654bf3c109d9481a5bd431e6453f6b7e5d14",
        ["src/DMO.Web/Frontend/Shared/Contracts/ToolSummaryFactPresentation.cs"] = "fb82db8caa26b43010fcd1e0f2beec83ac60fdad310ae50709c3ef520074e3db",
        ["src/DMO.Web/Frontend/Shared/Contracts/ToolSummaryRowEvent.cs"] = "5772090e9b364e33b5e64ccbfc20447325bdbd3952f40c9e4c0952b41202a527",
        ["src/DMO.Web/Frontend/Shared/Contracts/ToolSummaryRowEventKind.cs"] = "223c41142d9056d4f87b1ff193491a22c25ab43d1fc435c12a40c9c4347d2925",
        ["src/DMO.Web/Frontend/Shared/Contracts/ToolSummaryRowPresentation.cs"] = "0dcab9d1f61f52950138d93167b00c0bea3e4957264586541503c36e9717dff0",
        ["src/DMO.Web/Frontend/Shared/SharedFrontendExtensions.cs"] = "bdc6be9c4cd6cb2382e953446c123100e6065b8dc58b2fa70f793522fed1b5f8",
        ["src/DMO.Web/Pages/Shared/_Identity.cshtml"] = "2b934846d924ea02744b1bff50cc2176a935a7550c6c99175134ddabefbc24e3",
        ["src/DMO.Web/Pages/Shared/_Layout.cshtml"] = "ee18683cd7ca682deebd7cabbc207f6f44335f2d070efc3473dbae49478631a3",
        ["src/DMO.Web/Pages/Shared/_PublicLayout.cshtml"] = "042ae1d3fdebd79280a1e9d012437213b453022bc4d45fd9a7e6fdc7cc56083a",
        ["src/DMO.Web/Pages/Shared/Components/_AuditTrail.cshtml"] = "195832bba3be9ee3f611a9dc4ae0549322901fc0f237ec68351f4a6f31e8b178",
        ["src/DMO.Web/Pages/Shared/Components/_AvailabilityState.cshtml"] = "cb62da65fbccf79360ecd4092a9fda83ec7c3cf2c05399ab3146f3e467514c61",
        ["src/DMO.Web/Pages/Shared/Components/_CommonStateRegion.cshtml"] = "fbf796578aaa6cc36967d14b98a9a138e8c11d426a185283662103c92857d26d",
        ["src/DMO.Web/Pages/Shared/Components/_DecisionBar.cshtml"] = "44932d234b5064f261768b7681423fe5b144d295dc665b0979d55c3b5daafe77",
        ["src/DMO.Web/Pages/Shared/Components/_DenseDataTable.cshtml"] = "f81aab602c7d5ce4f1414789a2af0b0cf121a181e9887cb8b43932d29215e1a2",
        ["src/DMO.Web/Pages/Shared/Components/_MeasurementRows.cshtml"] = "25ddb298d742b187a583f5c34f66aad78c514a95623143b806393dcaacb330fb",
        ["src/DMO.Web/Pages/Shared/Components/_RecordStatus.cshtml"] = "147403ad555ab80f4202812552957cdf2e9bb85f9e5e2fd09f1fdb7d8299542a",
        ["src/DMO.Web/Pages/Shared/Components/_SharedComponentAssets.cshtml"] = "d4a5428a857c7df486bf2374d3fffe0bb66d68f334d8a74cf154fbbaa0697a34",
        ["src/DMO.Web/Pages/Shared/Components/_ToolPicker.cshtml"] = "5428c71cb7213fad0f73fff421a002bd8fc6676268f66ebcaf359bc65e5d7b3d",
        ["src/DMO.Web/Pages/Shared/Components/_ToolSummaryRow.cshtml"] = "9c7eeeaab46a52da970d67cbe151977b26a616a1d6b4999417ed6c8833f2d87f",
        ["src/DMO.Web/Pages/Shared/Navigation/_PrimaryNavigation.cshtml"] = "c33980af73d0b1013ba90d6928bb4c6d613455c410ec34f00ab4e33a88f13e90",
        ["src/DMO.Web/Pages/Shared/Navigation/_SecondaryNavigation.cshtml"] = "9d2f39462175a7eab749597c7e2ea21cb272de163f906baccafa918a8e9c5665",
        ["src/DMO.Web/wwwroot/css/dmo-admin-templates.css"] = "b1056f751557008961336d49dda58b775d714e9404a5e08f97ce8cffba673fd5",
        ["src/DMO.Web/wwwroot/css/dmo-admin-users.css"] = "29b0025fc50a5d1979d5c6123129d122f0db9b581632cf64d0d91e2fb89887ab",
        ["src/DMO.Web/wwwroot/css/dmo-components.css"] = "18135db5c3ca5c3f0073cbcd455d2b68c02361b47ff82366b96bf04e6dc6334c",
        ["src/DMO.Web/wwwroot/css/dmo-shell.css"] = "8ea26546a73d0ec3b0ba58b8f1315c13baeb8a729788274852ffc3d5e9ac228f",
        ["src/DMO.Web/wwwroot/css/dmo-tokens.css"] = "ccd80e3f8f6617849bf35c157c5567576494193df069e2e1d0a99f2f9732949d",
        ["src/DMO.Web/wwwroot/css/dmo-user-shell.css"] = "86e03c4ade02db887933551de5f9bb8cd59e91b73dea3590aeb38a36ae9d2249",
        ["src/DMO.Web/wwwroot/js/dmo-dense-table.js"] = "e351d82e636f70a89da3d83ff746e0196070c957d150b3dae1d3515927b627f2",
        ["src/DMO.Web/wwwroot/js/dmo-focus.js"] = "9e558f8b7284e6e933a61442c3c21e2a3e59c2c02410d8552ecc52a4cdd85b47",
        ["src/DMO.Web/wwwroot/js/dmo-measurement-rows.js"] = "2cd35963ca0ea469c0858b57ab9c1e066a1ca2d1249416cb0684a6a810ec28a5",
        ["src/DMO.Web/wwwroot/js/dmo-tool-picker.js"] = "7be8c6318ee68024ff5614442b6fea0ea2d8154a42e7353d5f6b92afe767c9be",
        ["tests/DMO.IntegrationTests/Frontend/Shared/P2T03Fixtures.cs"] = "35fee232f5661ea583126a16958d1d88c7c4ad100afae70de2b20eb04a32ed43",
    };

    /// <summary>
    /// The two accepted regression suites that must keep passing unmodified (contract §17.2 "tests/**
    /// existing tests: keep passing, never weaken" and AC-105), pinned by normalized content hash.
    /// </summary>
    private static readonly Dictionary<string, string> PinnedAcceptedRegressionTests = new(StringComparer.Ordinal)
    {
        ["tests/DMO.IntegrationTests/Frontend/Shared/P2T02RegressionTests.cs"] = "76eb1034fdde2c54ca021651cdcb8f883ef3eb81c7b6642bf99fbe179cedde8a",
        ["tests/DMO.IntegrationTests/Frontend/Shared/P2T03RegressionTests.cs"] = "dd98abee017090b7d2aadf619ca1f4ce20defcf32277dfff04984723c0288fa2",
    };

    /// <summary>
    /// The 13 canonical policy names of the frozen access contract (contract §13.4 and AC-101). Pinned
    /// literally so "no new policy" is proven by value, not only by the catalog hash.
    /// </summary>
    private static readonly string[] ExpectedPolicyNames =
    [
        "dmo.module.job-on-view", "dmo.module.job-on-create", "dmo.module.controlo-create",
        "dmo.module.controlo-approve", "dmo.module.reparacao-interna", "dmo.module.boquilhas",
        "dmo.module.armazem", "dmo.module.reparacao-programada-view",
        "dmo.module.reparacao-programada-create", "dmo.module.tampoes", "dmo.module.historia",
        "dmo.module.ferramentas", "dmo.module.ferramentas-approve",
    ];

    /// <summary>The canonical <c>job-on</c> destination id of the Job On Modules (contract §13.1).</summary>
    private const string JobOnDestinationId = "job-on";

    /// <summary>
    /// BND1 — the current-build availability list is still honest: the explicit registration list is
    /// non-null and empty, and the production composition resolves an <c>IModuleRegistry</c> whose
    /// definition count is zero because that empty list is its only source (contract §13.5
    /// consequence 4, §13.6; AC-92).
    /// </summary>
    [Fact]
    public void BND1_CurrentBuildAvailableIsStillEmptyAndTheProductionRegistryIsSourcedFromIt()
    {
        Assert.NotNull(ModuleRegistrations.CurrentBuildAvailable);
        Assert.Empty(ModuleRegistrations.CurrentBuildAvailable);

        using var factory = new DmoWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<IModuleRegistry>();

        Assert.IsType<ModuleRegistry>(registry);
        Assert.Empty(registry.AvailableModules);
        Assert.Equal(ModuleRegistrations.CurrentBuildAvailable, registry.AvailableModules);

        // The canonical vocabulary is still the full 13 identities: nothing was removed to make the
        // empty availability list look consistent.
        Assert.Equal(13, registry.KnownModuleIds.Count);
    }

    /// <summary>
    /// RTE11 — in the <b>production</b> composition the build-availability list is still empty and
    /// every P2-T04 Module resolves as known-but-unavailable, so a granted P2-T04 Module cannot become
    /// reachable before P2-T10 registers availability (contract §13.5, §13.6; AC-92, AC-94).
    /// </summary>
    [Fact]
    public void RTE11_CurrentBuildAvailableIsStillEmptyInTheProductionComposition()
    {
        using var factory = new DmoWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<IModuleRegistry>();

        Assert.Empty(ModuleRegistrations.CurrentBuildAvailable);
        Assert.Empty(registry.AvailableModules);

        foreach (var p2t04Module in new[]
                 {
                     ModuleCatalog.JobOnView, ModuleCatalog.JobOnCreate, ModuleCatalog.Ferramentas,
                 })
        {
            Assert.False(registry.IsAvailable(p2t04Module));
            Assert.IsType<ModuleResolve.KnownUnavailable>(registry.Resolve(p2t04Module.Value));
        }

        // No availability entry is invented for Ferramentas either: it stays a known contextual
        // identity that this build does not make available.
        Assert.False(registry.IsAvailable(ModuleCatalog.FerramentasApprove));
    }

    /// <summary>
    /// RTE12 — the destination-route seam is untouched: <c>DestinationRouteRegistrations</c> still
    /// declares no member at all, the production registration is still
    /// <c>EmptyDestinationRouteRegistry</c>, and no P2-T04 path is registered as a destination route
    /// (contract §13.5, §13.6, §17.3; AC-91).
    /// </summary>
    [Fact]
    public void RTE12_DestinationRouteRegistrationsStaysEmptyAndNoP2T04RouteIsRegistered()
    {
        var seam = typeof(DestinationRouteRegistrations);
        var flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        Assert.Empty(seam.GetFields(flags));
        Assert.Empty(seam.GetProperties(flags));
        Assert.DoesNotContain(seam.GetMethods(flags), method => !method.IsSpecialName);

        using var factory = new DmoWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<IDestinationRouteRegistry>();

        Assert.IsType<EmptyDestinationRouteRegistry>(registry);

        // The canonical P2-T04 destination id, the P2-T04 base paths themselves, the contextual
        // Ferramentas identity and both P2-T04 Module ids: none of them is a registered route.
        foreach (var destinationId in new[]
                 {
                     JobOnDestinationId, "ferramentas", "ferramentas-approve", "/jobon", "/ferramentas",
                     "job-on-view", "job-on-create", "jobon",
                 })
        {
            Assert.False(registry.TryGetRoute(destinationId, out var route));
            Assert.Equal(string.Empty, route);
        }
    }

    /// <summary>
    /// RTE14 — with the P2-T04 test-host composition (all three P2-T04 Modules granted and available,
    /// the test registry carrying <b>no</b> destination route) the real projection yields no
    /// <c>ferramentas</c> entry and no <c>job-on</c> entry, and Ferramentas can never appear
    /// top-level because its catalog entry has no destination id (contract §13.5 consequence 2,
    /// §13.6, §17.3; AC-91, AC-93).
    /// </summary>
    [Fact]
    public async Task RTE14_TestHostProjectionYieldsNoFerramentasAndNoJobOnEntry()
    {
        var store = new P2T04TestStore();
        using var factory = P2T04TestHost.ForUser(P2T04TestHost.AllGranted(), store);
        using var scope = factory.Services.CreateScope();

        // The documented reason for the absent entry: the composition registers no destination route.
        var routes = scope.ServiceProvider.GetRequiredService<IDestinationRouteRegistry>();

        Assert.False(routes.TryGetRoute(JobOnDestinationId, out _));
        Assert.False(routes.TryGetRoute("ferramentas", out _));

        var current = await scope.ServiceProvider
            .GetRequiredService<ICurrentAccountContext>()
            .GetCurrentAsync(CancellationToken.None);

        var presentation = await scope.ServiceProvider
            .GetRequiredService<NavigationProjectionService>()
            .ProjectAsync(current, CancellationToken.None);

        // Zero primary destinations: granted ∩ available ∩ non-contextual ∩ routed is empty here
        // because the routed term is empty.
        Assert.False(presentation.AccessResolutionFailed);
        Assert.Empty(presentation.LiveDestinations);
        Assert.DoesNotContain(presentation.LiveDestinations, entry => entry.DestinationId == JobOnDestinationId);
        Assert.DoesNotContain(presentation.LiveDestinations, entry => entry.DestinationId == "ferramentas");
        Assert.DoesNotContain(presentation.LiveDestinations, entry => entry.Label == "Ferramentas");
        Assert.DoesNotContain(
            presentation.LiveDestinations,
            entry => entry.GrantedModuleIds.Contains(ModuleCatalog.Ferramentas));

        // Ferramentas is contextual by catalog: it has no destination id, so no route registration
        // could ever promote it into the primary navigation.
        var ferramentas = ModuleCatalog.All.Single(entry => entry.Id == ModuleCatalog.Ferramentas);

        Assert.Null(ferramentas.DestinationId);
    }

    /// <summary>
    /// RTE15 — the access/authorization foundation is byte-identical to the pinned baseline and P2-T04
    /// introduced no policy: the policy names derived from <c>ModuleCatalog.All</c> are exactly the 13
    /// frozen canonical names (contract §13.2 policy statement, §13.4, §17.2; AC-101).
    /// </summary>
    [Fact]
    public void RTE15_ModuleCatalogAndModuleAuthorizationPoliciesAreByteIdenticalToTheBaseline()
    {
        foreach (var (relativePath, expectedHash) in PinnedAccessFoundation)
        {
            Assert.True(
                P2T04ProductionScan.Exists(relativePath),
                $"Protected access-foundation file '{relativePath}' is missing.");

            Assert.Equal(expectedHash, P2T04ProductionScan.HashFile(relativePath));
        }

        var policyNames = ModuleCatalog.All
            .Select(entry => ModuleAuthorizationPolicies.PolicyName(entry.Id))
            .ToList();

        Assert.Equal(13, ModuleCatalog.All.Count);
        Assert.Equal(13, policyNames.Count);
        Assert.Equal(13, policyNames.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            ExpectedPolicyNames.OrderBy(name => name, StringComparer.Ordinal),
            policyNames.OrderBy(name => name, StringComparer.Ordinal));

        // No P2-T04-specific policy exists: every derived name is a canonical Module policy, and no
        // name is a Tool/Job On surface identity of its own.
        Assert.All(policyNames, name => Assert.True(
            name.StartsWith("dmo.module.", StringComparison.Ordinal),
            $"Policy '{name}' is not in the canonical Module policy namespace."));

        Assert.DoesNotContain(policyNames, name => name.Contains("tool", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            policyNames,
            name => name.Contains(string.Concat("job", "on"), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// BND2 — the persistence foundation is byte-identical: <c>DmoDbContext.cs</c> is deliberately not
    /// modified and migrations 001/002 (and their EF designers) are never edited (contract §16.3
    /// rule 1, §16.5; AC-96).
    /// </summary>
    [Fact]
    public void BND2_Migration001And002AndDmoDbContextAreByteIdenticalToTheirPinnedBaseline()
    {
        Assert.Equal(5, PinnedProtectedFoundation.Count);

        foreach (var (relativePath, expectedHash) in PinnedProtectedFoundation)
        {
            Assert.True(
                P2T04ProductionScan.Exists(relativePath),
                $"Protected persistence-foundation file '{relativePath}' is missing.");

            Assert.Equal(expectedHash, P2T04ProductionScan.HashFile(relativePath));
        }

        // The single P2-T04 migration is a new file; the two protected migrations were not extended by
        // it. The upgraded model configures the six tables through the new migration only.
        Assert.True(P2T04ProductionScan.Exists(
            "src/DMO.Infrastructure/Migrations/20260922232349_ToolJobOnDomainCore.cs"));
    }

    /// <summary>
    /// BND3 — every pinned P2-T01/P2-T02/P2-T03 contract source, shared partial, stylesheet, script and
    /// the P2-T03 fixture is byte-identical, the P2-T03 artifact lists all still exist, and no P2-T04
    /// CSS was appended to <c>dmo-components.css</c> (contract §17.2, §18.2 rules 1, 5 and 6; AC-97).
    /// </summary>
    [Fact]
    public void BND3_EveryP2T01P2T02P2T03ArtifactIsByteIdenticalAndComponentsCssCarriesNoP2T04Marker()
    {
        // The accepted P2-T01 pins, read by reflection exactly as P2T03RegressionTests.RG3 does.
        var artifactsField = typeof(P2T02RegressionTests).GetField(
            "FrozenP2T01Artifacts", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(artifactsField);

        var frozenP2T01 = (Dictionary<string, string>)artifactsField!.GetValue(null)!;

        Assert.NotEmpty(frozenP2T01);

        foreach (var (relativePath, expectedHash) in frozenP2T01)
        {
            Assert.True(
                P2T04ProductionScan.Exists(relativePath),
                $"Accepted P2-T01 artifact '{relativePath}' is missing.");

            Assert.Equal(expectedHash, P2T04ProductionScan.HashFile(relativePath));
        }

        // Every pinned P2-T01/P2-T02/P2-T03 shared-frontend artifact is byte-identical.
        foreach (var (relativePath, expectedHash) in PinnedSharedFrontendArtifacts)
        {
            Assert.True(
                P2T04ProductionScan.Exists(relativePath),
                $"Accepted shared-frontend artifact '{relativePath}' is missing.");

            Assert.Equal(expectedHash, P2T04ProductionScan.HashFile(relativePath));
        }

        // The accepted P2-T03 artifact lists are unchanged in size, still present, and every one of
        // them is covered by a pinned hash.
        var p2t03Artifacts = P2T03ProductionScan.ContractSourcePaths
            .Concat(P2T03ProductionScan.PartialPaths)
            .Concat(P2T03ProductionScan.AssetPaths)
            .Concat(P2T03ProductionScan.FixturePaths)
            .ToList();

        Assert.Equal(30, P2T03ProductionScan.ContractSourcePaths.Count);
        Assert.Equal(4, P2T03ProductionScan.PartialPaths.Count);
        Assert.Equal(2, P2T03ProductionScan.AssetPaths.Count);
        Assert.Single(P2T03ProductionScan.FixturePaths);

        foreach (var artifact in p2t03Artifacts)
        {
            Assert.True(P2T04ProductionScan.Exists(artifact), $"P2-T03 artifact '{artifact}' is missing.");
            Assert.Contains(artifact, PinnedSharedFrontendArtifacts.Keys);
        }

        // P2-T04 keeps its own stylesheet: nothing was appended to the frozen component stylesheet.
        var componentsCss = P2T04ProductionScan.Read("src/DMO.Web/wwwroot/css/dmo-components.css");

        Assert.DoesNotContain("P2-T04", componentsCss, StringComparison.Ordinal);
        Assert.DoesNotContain("P2T04", componentsCss, StringComparison.Ordinal);
        Assert.DoesNotContain("dmo-jobon", componentsCss, StringComparison.Ordinal);
    }

    /// <summary>
    /// BND4 — the new P2-T04 stylesheet is non-empty and, comments stripped, carries no structural
    /// breakpoint rule and no width listener; the new P2-T04 script installs the accepted idempotency
    /// guard, reuses the frozen <c>window.dmoFocus</c> helper and declares no second focus helper
    /// (contract §18.2 rules 5 and 6, Appendix C; AC-102).
    /// </summary>
    [Fact]
    public void BND4_NewStylesheetAndScriptAreBreakpointFreeIdempotentAndReuseTheFrozenFocusHelper()
    {
        var cssPath = "src/DMO.Web/wwwroot/css/dmo-jobon.css";

        Assert.True(P2T04ProductionScan.Exists(cssPath), $"'{cssPath}' is missing.");

        var css = P2T04ProductionScan.Read(cssPath);

        Assert.False(string.IsNullOrWhiteSpace(css), "The P2-T04 stylesheet must not be empty.");

        var rules = P2T04ProductionScan.WithoutCssComments(css);

        Assert.False(string.IsNullOrWhiteSpace(rules), "The P2-T04 stylesheet must carry real rules.");

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

        var jsPath = "src/DMO.Web/wwwroot/js/dmo-jobon.js";

        Assert.True(P2T04ProductionScan.Exists(jsPath), $"'{jsPath}' is missing.");

        var script = P2T04ProductionScan.Read(jsPath);

        Assert.NotEmpty(script);

        // The accepted idempotency guard.
        Assert.Contains("if (window.dmoJobOn)", script, StringComparison.Ordinal);

        // Focus restoration is delegated to the frozen helper, not re-implemented.
        Assert.Contains("window.dmoFocus", script, StringComparison.Ordinal);
        Assert.DoesNotContain("function dmoFocus", script, StringComparison.Ordinal);
        Assert.DoesNotContain("dmoFocus =", script, StringComparison.Ordinal);
        Assert.DoesNotContain("dmoFocus:", script, StringComparison.Ordinal);

        // No width listener in the new script either.
        foreach (var widthCondition in new[]
                 {
                     "matchMedia", "ResizeObserver", "addEventListener('resize'", "innerWidth",
                 })
        {
            Assert.DoesNotContain(widthCondition, script, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// BND5 — no fake or legacy production identity appears anywhere in the P2-T04 sources: the
    /// composed <c>production_id</c>/<c>job_on_revision_id</c>/<c>productionId</c>/
    /// <c>jobOnRevisionId</c>/<c>revisionId</c> tokens occur nowhere in code, schema text or payload
    /// (contract §17.1; AC-98).
    /// </summary>
    [Fact]
    public void BND5_NoLegacyOrFakeProductionIdentityTokenAppearsInAnyP2T04Source()
    {
        Assert.NotEmpty(P2T04ProductionScan.ProductionSourcePaths);

        foreach (var token in P2T04ProductionScan.LegacyIdentityTokens)
        {
            var offenders = P2T04ProductionScan.ProductionSourcesMentioningInCode(token);

            Assert.True(
                offenders.Count == 0,
                $"The legacy identity token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }
    }

    /// <summary>
    /// BND6 — no repairer, machine-assignment, machine-registry or Definições vocabulary appears in the
    /// P2-T04 sources or schema: no standalone <c>machine_id</c>/<c>machineid</c> member, no
    /// <c>machines</c> table, no Linha B/C grouping and no <c>defini</c>/<c>settings</c> concept. The
    /// machine CODE column and the legitimate <c>tool_machines</c> table are not flagged (contract
    /// §17.1; AC-99).
    /// <para>
    /// <b>Outputs slice disclosed extension</b> (Controlo outputs on the Job On sheet — this
    /// slice): the workspace hint of the outputs section necessarily NAMES the configured settings
    /// surface it points the operator to (<c>Controlo_Create → Definições</c>) — the single
    /// <c>defini</c> mention in markup on the Job On sheet, disclosed here instead of degrading the
    /// operator-facing hint. The disclosure is asserted non-vacuous below.
    /// </para>
    /// </summary>
    [Fact]
    public void BND6_NoRepairerMachineAssignmentMachineRegistryOrDefinicoesVocabularyAppears()
    {
        foreach (var token in P2T04ProductionScan.RepairerAndMachineBoundaryTokens)
        {
            var offenders = P2T04ProductionScan.ProductionSourcesMentioningInCode(token);

            Assert.True(
                offenders.Count == 0,
                $"The excluded repairer/machine token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }

        foreach (var token in P2T04ProductionScan.QuotedSchemaTokens)
        {
            var offenders = P2T04ProductionScan.ProductionSourcePaths
                .Where(path => P2T04ProductionScan.Read(path)
                    .Contains(token, StringComparison.Ordinal))
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The excluded schema name {token} is declared in: {string.Join(", ", offenders)}.");
        }

        // The outputs-slice disclosed extension: the ONE Job On sheet file whose markup names the
        // configured settings surface in its workspace hint.
        var disclosedOutputsSliceHintPaths = new[]
        {
            "src/DMO.Web/Pages/JobOn/View.cshtml",
        };

        foreach (var fragment in P2T04ProductionScan.DefiniSettingsFragments)
        {
            var offenders = P2T04ProductionScan.ProductionSourcesMentioningFragment(fragment)
                .Where(path => !disclosedOutputsSliceHintPaths.Contains(path, StringComparer.Ordinal))
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The excluded fragment '{fragment}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }

        // The disclosure is real, not vacuous: the disclosed sheet file exists and its outputs
        // section actually carries the workspace hint naming the settings surface.
        foreach (var path in disclosedOutputsSliceHintPaths)
        {
            Assert.True(P2T04ProductionScan.Exists(path), $"Disclosed outputs-slice path '{path}' is missing.");

            var source = P2T04ProductionScan.Read(path);

            Assert.True(
                source.Contains(string.Concat("Defini", "\u00e7\u00f5es"), StringComparison.Ordinal),
                $"Disclosed outputs-slice path '{path}' does not carry the disclosed settings hint.");
            Assert.True(
                source.Contains("data-dmo-jobon-outputs", StringComparison.Ordinal),
                $"Disclosed outputs-slice path '{path}' does not carry the outputs section.");
        }

        // Schema-level proof: the P2-T04 migration creates exactly the six contracted tables and no
        // machines table.
        var createdTables = P2T04ProductionScan.CreatedTableNames();

        Assert.Equal(
            new[] { "bq_contexts", "cm_contexts", "job_ons", "mf_contexts", "tool_machines", "tools" },
            createdTables.ToArray());

        Assert.DoesNotContain(createdTables, table =>
            table.Equals("machines", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(createdTables, table =>
            table.Contains("repair", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// BND7 — no P2-T05-or-later concept appears in any P2-T04 source: Definições, Peso, Comparação,
    /// Pegamentos, Folha, Resumo, Boquilhas, PDF, HISTÓRICO GLOBAL, secondary navigation, module
    /// availability registration or a destination-route/navigation reference (contract §17.4; AC-100).
    /// The legitimate <c>bq_contexts</c>/BQ vocabulary is deliberately not flagged.
    /// <para>
    /// <b>P2-T05 disclosed exclusion</b> (accepted Q-CAND, P2-T05 contract §20.4.2): the ONE
    /// cross-stream additive read-only member on the Job On application contract
    /// (<c>ListPesoAssociationCandidatesAsync</c>) necessarily lands in
    /// <c>IJobOnService.cs</c>/<c>JobOnService.cs</c>/<c>JobOnModels.cs</c> (method, implementation,
    /// candidate record + result case). Those three files are excluded from this row's vocabulary
    /// scan — the exclusion is asserted to be exactly the three sanctioned files AND to carry the
    /// additive vocabulary (never vacuous).
    /// </para>
    /// <para>
    /// <b>Outputs slice disclosed extension</b> (Controlo outputs on the Job On sheet — this
    /// slice: the Peso PDF): exposing the existing Controlo outputs on the Job On surface
    /// NECESSARILY carries the Peso/PDF vocabulary of what it exposes — the outputs read
    /// projection and service (the <c>jobon_id → peso_id</c> relation), the open route of the
    /// controlled <c>peso-pdf</c> endpoint and the outputs section of the sheet. Those Job On
    /// owned files are excluded from this row's vocabulary scan and each is asserted to actually
    /// carry the additive outputs vocabulary (never vacuous). No other P2-T04 source gains the
    /// later-workstream vocabulary.
    /// </para>
    /// </summary>
    [Fact]
    public void BND7_NoP2T05OrLaterVocabularyAppearsInAnyP2T04Source()
    {
        var disclosedCrossStreamAdditivePaths = new[]
        {
            "src/DMO.Application/JobOn/IJobOnService.cs",
            "src/DMO.Application/JobOn/JobOnService.cs",
            "src/DMO.Application/JobOn/JobOnModels.cs",
        };

        // The outputs-slice additive files: the Job On owned files that necessarily carry the
        // Peso/PDF vocabulary of the outputs they expose (read projection, service contract,
        // implementation, open route, sheet markup and page model).
        var disclosedOutputsSlicePaths = new[]
        {
            "src/DMO.Application/JobOn/IJobOnControlOutputsService.cs",
            "src/DMO.Application/JobOn/JobOnControlOutputsService.cs",
            "src/DMO.Application/JobOn/JobOnControlOutputsModels.cs",
            "src/DMO.Web/Endpoints/ToolJobOn/JobOnEndpoints.cs",
            "src/DMO.Web/Pages/JobOn/View.cshtml",
            "src/DMO.Web/Pages/JobOn/View.cshtml.cs",
        };

        var disclosed = disclosedCrossStreamAdditivePaths
            .Concat(disclosedOutputsSlicePaths)
            .ToList();

        foreach (var token in P2T04ProductionScan.LaterWorkstreamTokens)
        {
            var offenders = P2T04ProductionScan
                .ProductionSourcesMentioningInCode(token)
                .Where(path => !disclosed.Contains(path, StringComparer.Ordinal))
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"The P2-T05+ token '{token}' appears in code/markup of: {string.Join(", ", offenders)}.");
        }

        // The Q-CAND exclusion is real, not vacuous: every disclosed file exists and carries the
        // sanctioned additive vocabulary (the Q-CAND candidate read), and it is exactly the three
        // files — no other P2-T04 source carries the Peso vocabulary outside the scan.
        foreach (var path in disclosedCrossStreamAdditivePaths)
        {
            Assert.True(P2T04ProductionScan.Exists(path), $"Disclosed additive path '{path}' is missing.");

            var source = P2T04ProductionScan.Read(path);

            Assert.True(
                source.Contains(string.Concat("Peso", "Association", "Candidate"), StringComparison.Ordinal)
                || source.Contains("ListPesoAssociation", StringComparison.Ordinal),
                $"Disclosed additive path '{path}' does not carry the sanctioned additive member.");
        }

        // The outputs-slice exclusion is real, not vacuous: every disclosed file exists and carries
        // the additive outputs vocabulary (the control-outputs read or the controlled peso-pdf
        // open route of the sheet).
        var outputsSliceReadMarker = string.Concat("Job", "On", "Control", "Outputs");
        var outputsSliceRouteMarker = string.Concat("peso", "-pdf");
        var outputsSliceVocabularyMarker = string.Concat("Pe", "so");

        foreach (var path in disclosedOutputsSlicePaths)
        {
            Assert.True(P2T04ProductionScan.Exists(path), $"Disclosed outputs-slice path '{path}' is missing.");

            var source = P2T04ProductionScan.Read(path);

            Assert.True(
                source.Contains(outputsSliceReadMarker, StringComparison.Ordinal)
                || source.Contains(outputsSliceRouteMarker, StringComparison.Ordinal)
                || source.Contains(outputsSliceVocabularyMarker, StringComparison.Ordinal),
                $"Disclosed outputs-slice path '{path}' does not carry the sanctioned additive vocabulary.");
        }

        var allPesoMentions = P2T04ProductionScan.ProductionSourcePaths
            .Where(path => P2T04ProductionScan.CodeOccurrences(
                P2T04ProductionScan.Read(path),
                string.Concat("Pe", "so")).Count > 0)
            .ToList();

        Assert.All(
            allPesoMentions,
            path => Assert.True(
                disclosed.Contains(path, StringComparer.Ordinal),
                $"P2-T04 source '{path}' carries Peso vocabulary outside the disclosed Q-CAND/outputs-slice files."));

        // The legitimate BQ context vocabulary is present and must not be misread as a Boquilhas
        // aggregate: the P2-T04 sources do declare bq_contexts.
        var bqSources = P2T04ProductionScan.ProductionSourcePaths
            .Where(path => P2T04ProductionScan.Read(path)
                .Contains(string.Concat("bq", "_contexts"), StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(bqSources);
    }

    /// <summary>
    /// BND8 — nothing encodes Tool→JobOn navigation in the reverse direction: neither the domain
    /// <c>Tool</c> nor <c>ToolEntity</c> exposes a collection of JobOn/JobOnId/ToolContext members, and
    /// the <c>tools</c>/<c>tool_machines</c> configurations declare no navigation, no column and no
    /// join target pointing at <c>job_ons</c> (contract §17.1, §3.1, §3.2; AC-98).
    /// </summary>
    [Fact]
    public void BND8_NoReverseCollectionJoinTableOrConfigurationEncodesToolToJobOnNavigation()
    {
        var reverseMemberTokens = new[]
        {
            string.Concat("Job", "On"),
            string.Concat("Job", "On", "Id"),
            string.Concat("Tool", "Context"),
        };

        var reverseElementTokens = new[]
        {
            string.Concat("Job", "On"),
            string.Concat("Job", "On", "Id"),
            string.Concat("Tool", "Context"),
        };

        foreach (var type in new[] { typeof(Tool), typeof(ToolEntity) })
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            Assert.DoesNotContain(type.GetMembers(flags), member =>
                reverseMemberTokens.Any(token =>
                    member.Name.Contains(token, StringComparison.Ordinal)));

            var memberTypes = type.GetProperties(flags).Select(property => property.PropertyType)
                .Concat(type.GetFields(flags).Select(field => field.FieldType))
                .Concat(type.GetConstructors()
                    .SelectMany(constructor => constructor.GetParameters().Select(p => p.ParameterType)))
                .ToList();

            foreach (var memberType in memberTypes)
            {
                if (memberType == typeof(string)
                    || !typeof(System.Collections.IEnumerable).IsAssignableFrom(memberType))
                {
                    continue;
                }

                // A reverse navigation would be exactly a collection whose element is one of the
                // Job On / Tool-context types.
                foreach (var element in memberType.GetGenericArguments())
                {
                    Assert.DoesNotContain(reverseElementTokens, token =>
                        element.Name.Contains(token, StringComparison.Ordinal));
                }
            }
        }

        foreach (var configurationPath in new[]
                 {
                     "src/DMO.Infrastructure/Persistence/ToolJobOn/EntityConfigurations/ToolEntityConfiguration.cs",
                     "src/DMO.Infrastructure/Persistence/ToolJobOn/EntityConfigurations/ToolMachineEntityConfiguration.cs",
                 })
        {
            var source = P2T04ProductionScan.Read(configurationPath);

            Assert.DoesNotContain("job_ons", source, StringComparison.Ordinal);
            Assert.DoesNotContain("JobOn", source, StringComparison.Ordinal);

            // Every declared navigation of these two configurations targets a Tool-owned member.
            var navigations = Regex.Matches(
                    source,
                    @"\.(?:HasOne|HasMany|WithOne|WithMany)\([^)]*=>\s*[A-Za-z_]\w*\.(?<member>[A-Za-z_]\w*)")
                .Select(match => match.Groups["member"].Value)
                .ToList();

            Assert.All(navigations, navigation => Assert.Contains(navigation, new[] { "Tool", "Machines" }));

            // No column of the tools/tool_machines tables is a Job On reference.
            var columns = Regex.Matches(source, @"HasColumnName\(""(?<name>[A-Za-z0-9_]+)""\)")
                .Select(match => match.Groups["name"].Value)
                .ToList();

            Assert.NotEmpty(columns);
            Assert.DoesNotContain(columns, column =>
                column.StartsWith("job", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// BND9 — the changed-path allow-list holds: every file under <c>src</c> that mentions the P2-T04
    /// domain vocabulary is inside the P2-T04 owned paths, is one of the four documented additive
    /// files, or belongs to the P2-T05 owned surface (disclosed extension: the accepted P2-T05
    /// implementation legitimately consumes the P2-T04 anchors — <c>tool_id</c>/<c>cm_id</c> — inside
    /// its OWN paths; the P2-T05 allow-list itself is enforced by the P2-T05 BND9 row) or to the
    /// P2-T07 owned surface (disclosed extension, P2-T07 response: the Boquilhas surfaces consume
    /// the closed P2-T04 Tool/Job On/context facts by contract — the accepted read-only
    /// entity-set composition of review observation N1; the P2-T07 allow-list itself is enforced
    /// by the P2-T07 boundary rows). The protected <c>DmoDbContext.cs</c> carries no P2-T04
    /// vocabulary at all (contract §13.6, §17.2, Appendix B; AC-95).
    /// </summary>
    [Fact]
    public void BND9_TheChangedPathAllowListHoldsForEveryP2T04VocabularyMention()
    {
        var offenders = P2T04ProductionScan.VocabularyMentionsOutsideOwnedPaths()
            .Where(path => !IsPathInP2T05OwnedSurface(path))
            .Where(path => !IsPathInP2T07OwnedSurface(path))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Files outside the P2-T04 owned paths mention the P2-T04 vocabulary: {string.Join(", ", offenders)}.");

        // The allow-list is real, not vacuous: the vocabulary is present inside the owned paths and in
        // the documented additive composition files.
        foreach (var expected in new[]
                 {
                     "src/DMO.Web/Program.cs",
                     "src/DMO.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs",
                     "src/DMO.Infrastructure/Migrations/DmoDbContextModelSnapshot.cs",
                     "src/DMO.Infrastructure/Migrations/20260922232349_ToolJobOnDomainCore.cs",
                     "src/DMO.Web/Endpoints/ToolJobOn/JobOnEndpoints.cs",
                 })
        {
            Assert.True(P2T04ProductionScan.Exists(expected), $"Expected P2-T04 path '{expected}' is missing.");
        }

        // The protected context is the exception the allow-list must not excuse.
        var dbContext = P2T04ProductionScan.Read(P2T04ProductionScan.ProtectedDbContextPath);

        Assert.Empty(P2T04ProductionScan.CodeOccurrences(dbContext, string.Concat("tool", "_machines")));
        Assert.Empty(P2T04ProductionScan.CodeOccurrences(dbContext, string.Concat("job", "_on")));
        Assert.Empty(P2T04ProductionScan.CodeOccurrences(dbContext, string.Concat("job", "on")));
    }

    /// <summary>
    /// Whether a repository-relative path belongs to the P2-T05 owned surface (the disclosed
    /// extension of the P2-T04 changed-path allow-list). The P2-T05 surfaces consume the accepted
    /// P2-T04 anchors by contract, so their own paths are allowed to mention the P2-T04 vocabulary;
    /// the P2-T05 paths are themselves pinned by the P2-T05 BND9 row.
    /// </summary>
    private static bool IsPathInP2T05OwnedSurface(string relativePath) =>
        DMO.IntegrationTests.ControloCreate.P2T05ProductionScan.IsOwnedPath(relativePath);

    /// <summary>
    /// Whether a repository-relative path belongs to the P2-T07 owned surface (the disclosed
    /// extension of the P2-T04 changed-path allow-list). The Boquilhas surfaces consume the closed
    /// P2-T04 Tool/Job On/context facts by contract (the accepted read-only entity-set composition
    /// of review observation N1), so their own paths are allowed to mention the P2-T04 vocabulary;
    /// the P2-T07 paths are themselves pinned by the P2-T07 boundary rows.
    /// </summary>
    private static bool IsPathInP2T07OwnedSurface(string relativePath) =>
        DMO.IntegrationTests.Boquilhas.P2T07ProductionScan.IsOwnedPath(relativePath);

    /// <summary>
    /// BND10 — the accepted <c>DMO.UnitTests</c> surface survives: every test method pinned by the
    /// accepted <c>P2T02RegressionTests.FrozenTestMethods</c> dictionary still exists, and no
    /// pre-existing unit-test source carries P2-T04 domain vocabulary (contract §17.2, §18.3;
    /// AC-105).
    /// </summary>
    [Fact]
    public void BND10_TheAcceptedUnitTestMethodsSurviveAndNoUnitTestSourceCarriesP2T04Vocabulary()
    {
        var methodsField = typeof(P2T02RegressionTests).GetField(
            "FrozenTestMethods", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(methodsField);

        var frozenMethods = (Dictionary<Type, string[]>)methodsField!.GetValue(null)!;

        Assert.NotEmpty(frozenMethods);

        foreach (var (frozenType, methodNames) in frozenMethods)
        {
            Assert.NotEmpty(methodNames);
            Assert.All(
                methodNames,
                name => Assert.NotNull(
                    frozenType.GetMethod(name, BindingFlags.Public | BindingFlags.Instance)));
        }

        var offenders = P2T04ProductionScan.TestVocabularyOffenders(
            "tests/DMO.UnitTests", ".cs", ".cshtml");

        // The disclosed additive extensions (see DisclosedAdditiveTestPaths) are the only
        // pre-existing unit-test files allowed to mention the P2-T04 vocabulary; any other file
        // is a violation of the "existing tests are never edited" boundary. This mirrors the
        // BND11/BND12 disclosure pattern: the P2-T08 documents-test files legitimately carry the
        // canonical Tool identity tuple / MachineCode of the SHARED sheet fixtures, and that
        // omission left this row red since those slices — an additive repair, never a weakening.
        Assert.All(
            offenders,
            offender => Assert.True(
                P2T04ProductionScan.DisclosedAdditiveTestPaths.Contains(offender, StringComparer.Ordinal),
                $"Pre-existing unit-test source '{offender}' carries P2-T04 vocabulary."));
    }

    /// <summary>
    /// BND11 — the accepted <c>DMO.IntegrationTests</c> environment gate survives: every pre-existing
    /// DB-class call site of <c>PersistenceTestDatabase.SkipIfNotConfigured()</c> is still declared
    /// inside a skippable member (<c>[SkippableFact]</c>, or its theory form
    /// <c>[SkippableTheory]</c>), and no pre-existing integration-test source outside the new P2-T04
    /// folders carries P2-T04 domain vocabulary (contract §17.2; AC-105).
    /// </summary>
    [Fact]
    public void BND11_EveryPreExistingDbClassTestStaysGatedAndCarriesNoP2T04Vocabulary()
    {
        // The gate is actually exercised: at least one pre-existing file calls it.
        Assert.Contains(
            P2T04ProductionScan.PreExistingTestFiles("tests/DMO.IntegrationTests", ".cs"),
            path => P2T04ProductionScan.Read(path)
                .Contains(P2T04ProductionScan.PersistenceGateCall, StringComparison.Ordinal));

        var ungated = P2T04ProductionScan.UngatedPersistenceCallSites("tests/DMO.IntegrationTests");

        Assert.True(
            ungated.Count == 0,
            $"Environment-gated DB call sites without a skippable attribute: {string.Join(" | ", ungated)}.");

        var offenders = P2T04ProductionScan.TestVocabularyOffenders(
            "tests/DMO.IntegrationTests", ".cs", ".cshtml");

        // The single disclosed additive extension (see DisclosedAdditiveTestPaths) is the only
        // pre-existing test file allowed to mention the P2-T04 vocabulary; any other file is a
        // violation of the "existing tests are never edited" boundary.
        Assert.All(
            offenders,
            offender => Assert.True(
                P2T04ProductionScan.DisclosedAdditiveTestPaths.Contains(offender, StringComparer.Ordinal),
                $"Pre-existing integration-test source '{offender}' carries P2-T04 vocabulary."));
    }

    /// <summary>
    /// BND12 — no accepted test was weakened or deleted: the two accepted regression suites are
    /// byte-identical to their pinned baselines, and every other pre-existing test file either is
    /// covered by a pin or carries no P2-T04 vocabulary (contract §17.2; AC-105).
    /// </summary>
    /// <remarks>
    /// The disclosed additive extension <c>tests/DMO.IntegrationTests/MigrationRunnerTests.cs</c>
    /// (contract §3/§16.5: the six new tables are mapped by the same single <c>DmoDbContext</c>, so its
    /// exact modelled-table assertion necessarily grew by six names, documented in the file itself) is
    /// the one pre-existing test file this workstream modified. It is listed explicitly in
    /// <c>DisclosedAdditiveTestPaths</c> so this row fails if any <i>other</i> existing test file starts
    /// carrying P2-T04 vocabulary; the blanket "zero modified test files" form of the row is not
    /// asserted because that modification is disclosed and present.
    /// </remarks>
    [Fact]
    public void BND12_NoAcceptedTestFileWasWeakenedAndNoOtherExistingTestWasModified()
    {
        foreach (var (relativePath, expectedHash) in PinnedAcceptedRegressionTests)
        {
            Assert.True(
                P2T04ProductionScan.Exists(relativePath),
                $"Accepted regression suite '{relativePath}' is missing.");

            Assert.Equal(expectedHash, P2T04ProductionScan.HashFile(relativePath));
        }

        var offenders = P2T04ProductionScan.TestVocabularyOffenders("tests", ".cs");

        Assert.All(
            offenders,
            offender => Assert.True(
                P2T04ProductionScan.DisclosedAdditiveTestPaths.Contains(offender, StringComparer.Ordinal),
                $"Existing test file '{offender}' was modified with P2-T04 vocabulary and is not a disclosed additive extension."));

        // The new P2-T04 test surface is excluded from the scan above and must exist.
        Assert.True(P2T04ProductionScan.Exists("tests/DMO.IntegrationTests/JobOn/P2T04TestHost.cs"));
        Assert.True(P2T04ProductionScan.Exists("tests/DMO.IntegrationTests/JobOn/P2T04TestStore.cs"));
    }

    /// <summary>
    /// BND13 — behavioural proof that no P2-T04 route registers module availability or a destination
    /// route: with the P2-T04 test host composed, the P2-T04 paths exist in the real
    /// <c>EndpointDataSource</c>, the build-availability list is unchanged after the host is built, and
    /// the resolved destination-route registry still has no route for <c>job-on</c> or
    /// <c>ferramentas</c> (contract §13.5, §13.6, §13.2 route-count statement; AC-101).
    /// </summary>
    [Fact]
    public async Task BND13_NoP2T04RouteRegistersModuleAvailabilityOrADestinationRoute()
    {
        var availabilityBeforeTheHostWasBuilt = ModuleRegistrations.CurrentBuildAvailable.ToArray();

        var store = new P2T04TestStore();
        using var factory = P2T04TestHost.ForUser(P2T04TestHost.AllGranted(), store);

        var patterns = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToList();

        // The physical P2-T04 surface is mapped (contract §13.2 routes 1–6 and 12/13). A physical page
        // existing is exactly what must NOT make a Module available or a destination reachable.
        foreach (var expected in new[]
                 {
                     "/jobon", "/jobon/productions", "/jobon/create/productions", "/ferramentas/tools",
                 })
        {
            Assert.Contains(
                patterns,
                pattern => string.Equals(pattern.TrimStart('/',' '), expected.TrimStart('/',' '), StringComparison.OrdinalIgnoreCase));
        }

        Assert.Contains(patterns, pattern => pattern.StartsWith("/jobon/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(patterns, pattern => pattern.StartsWith("/ferramentas/", StringComparison.OrdinalIgnoreCase));

        // And the surface really answers: a fully granted caller gets the permitted page.
        using var client = factory.CreateClient();
        using var response = await P2T04TestHost.GetAsync(client, "/jobon");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Required non-effects, after the whole host has been composed and served a request.
        Assert.Empty(ModuleRegistrations.CurrentBuildAvailable);
        Assert.Equal(availabilityBeforeTheHostWasBuilt, ModuleRegistrations.CurrentBuildAvailable);

        using var scope = factory.Services.CreateScope();
        var routes = scope.ServiceProvider.GetRequiredService<IDestinationRouteRegistry>();

        Assert.False(routes.TryGetRoute(JobOnDestinationId, out _));
        Assert.False(routes.TryGetRoute("ferramentas", out _));
        Assert.False(routes.TryGetRoute("/jobon", out _));
        Assert.False(routes.TryGetRoute("/ferramentas", out _));
    }

    /// <summary>
    /// BND14 — the two accepted non-new composition files received exactly the contracted registration
    /// lines and nothing else P2-T04-related: <c>Program.cs</c> carries the two service registrations
    /// and the two endpoint mappings (and no availability entry, no <c>ModuleCatalog</c> addition and no
    /// destination-route registration), and <c>PersistenceServiceCollectionExtensions.cs</c> carries the
    /// two repository registrations and the one dependency-probe registration (contract §12.5, §13.6,
    /// §17.2; AC-95, AC-101).
    /// </summary>
    [Fact]
    public void BND14_AdditiveCompositionFilesCarryExactlyTheContractedRegistrationLines()
    {
        const string toolService = "builder.Services.AddScoped<IToolService, ToolService>();";
        const string jobOnService = "builder.Services.AddScoped<IJobOnService, JobOnService>();";
        const string mapJobOn = "app.MapJobOnEndpoints();";
        const string mapFerramentas = "app.MapFerramentasEndpoints();";

        var program = P2T04ProductionScan.Read("src/DMO.Web/Program.cs");
        var programLines = program.Split('\n').Select(line => line.Trim()).ToList();

        foreach (var contracted in new[] { toolService, jobOnService, mapJobOn, mapFerramentas })
        {
            Assert.Equal(1, programLines.Count(line => line.Equals(contracted, StringComparison.Ordinal)));
        }

        foreach (var forbidden in new[]
                 {
                     string.Concat("Module", "Registrations"),
                     string.Concat("CurrentBuild", "Available"),
                     string.Concat("DestinationRoute", "Registrations"),
                     string.Concat("IDestinationRoute", "Registry"),
                     string.Concat("EmptyDestinationRoute", "Registry"),
                     "ModuleRegistry",
                     string.Concat("Navigation", "ProjectionService"),
                 })
        {
            Assert.DoesNotContain(forbidden, program, StringComparison.Ordinal);
        }

        // The P2-T04 mentions in Program.cs are exactly the two imports plus the four contracted
        // lines: no repository, no probe, no entity, no P2-T04 page is registered there.
        Assert.Equal(
            new[]
            {
                "using DMO.Application.JobOn;",
                "using DMO.Application.Tools;",
                toolService,
                jobOnService,
                mapJobOn,
                mapFerramentas,
            }.OrderBy(line => line, StringComparer.Ordinal),
            programLines
                .Where(line => P2T04ProductionScan.TestVocabulary.Any(token =>
                    line.Contains(token, StringComparison.Ordinal)))
                .OrderBy(line => line, StringComparer.Ordinal));

        const string toolRepository = "services.AddScoped<IToolRepository, ToolRepository>();";
        const string jobOnRepository = "services.AddScoped<IJobOnRepository, JobOnRepository>();";
        const string dependencyProbe =
            "services.AddScoped<IJobOnDependencyProbe, JobOnLineageDependencyProbe>();";

        var persistence = P2T04ProductionScan.Read(
            "src/DMO.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs");
        var persistenceLines = persistence.Split('\n').Select(line => line.Trim()).ToList();

        foreach (var contracted in new[] { toolRepository, jobOnRepository, dependencyProbe })
        {
            Assert.Equal(1, persistenceLines.Count(line => line.Equals(contracted, StringComparison.Ordinal)));
        }

        Assert.Equal(
            new[]
            {
                "using DMO.Application.JobOn;",
                toolRepository,
                jobOnRepository,
                dependencyProbe,
                // Disclosed P2-T05 extension (P2-T05 contract §20.4.1/§20.4.2): the Peso
                // dependency probe registers through the same one-additive-line seam, and the
                // Job On region gains no other vocabulary-bearing line.
                "services.AddScoped<IJobOnDependencyProbe, PesoJobOnDependencyProbe>();",
                // Disclosed P2-T07 extension (P2-T07 contract §27/App. A): the Boquilhas
                // dependency probe registers through the same one-additive-line seam.
                "services.AddScoped<IJobOnDependencyProbe, BoquilhasDependencyProbe>();",
            }.OrderBy(line => line, StringComparer.Ordinal),
            persistenceLines
                .Where(line => P2T04ProductionScan.TestVocabulary.Any(token =>
                    line.Contains(token, StringComparison.Ordinal)))
                .OrderBy(line => line, StringComparer.Ordinal));
    }
}
