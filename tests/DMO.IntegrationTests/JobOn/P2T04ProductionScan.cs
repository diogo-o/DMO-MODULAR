using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DMO.IntegrationTests.JobOn;

/// <summary>
/// P2-T04 boundary/regression scan helper: the repository-root walk, the normalized content hash, the
/// comment-aware token scan, the P2-T04 production path tables and the ownership/vocabulary tables
/// used by the boundary rows BND1–BND14 and the route rows RTE11/RTE12/RTE14/RTE15.
/// </summary>
/// <remarks>
/// <para>
/// Authority: P2-T04 contract §13.5 (interim runtime state), §13.6 (routing boundary), §16 (migration
/// contract), §17 (explicit non-scope), §17.2 (protected files), §18 (P2-T03 composition), §20.6 rows
/// RTE11/RTE12/RTE14/RTE15, §20.8 rows BND1–BND14 and AC-91 … AC-106.
/// </para>
/// <para>
/// The forbidden-vocabulary tables are composed from parts, exactly like the accepted
/// <c>P2T03ProductionScan</c>/<c>P2T03TypeScan</c> helpers, so this assertion source never carries a
/// composed literal of its own. This helper carries no test and asserts nothing: the rows live in
/// <c>P2T04RegressionTests</c>.
/// </para>
/// <para>
/// <b>Comment-aware scanning.</b> The P2-T04 sources document the *absence* of the excluded concepts
/// (for example "there is no <c>machine_id</c> scheme"), so a naive substring scan over the raw file
/// text measures documentation, not behaviour. <see cref="CodeOccurrences"/> therefore reports only
/// occurrences that lie outside C# line/block comments, Razor comments and CSS comments — that is,
/// occurrences in code, markup, schema text or payload, which is precisely what AC-98/AC-99/AC-100
/// exclude. <see cref="SubstringOccurrences"/> is the same rule for tokens that must be matched as a
/// fragment (for example <c>defini</c>, which must also catch <c>Definições</c>/<c>definition</c>).
/// </para>
/// </remarks>
internal static class P2T04ProductionScan
{
    // ================= repository access =================================================

    /// <summary>Finds the repository root by the solution marker.</summary>
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DMO.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>Reads one repository-relative path.</summary>
    public static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Reads the supplied repository-relative paths, joined by a newline.</summary>
    public static string ReadAll(IEnumerable<string> relativePaths) =>
        string.Join('\n', relativePaths.Select(Read));

    /// <summary>Returns whether one repository-relative path exists.</summary>
    public static bool Exists(string relativePath) =>
        File.Exists(Path.Combine(
            RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>The accepted normalized content hash: CRLF normalized to LF, SHA-256, lowercase hex.</summary>
    public static string NormalizedHash(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        return Convert.ToHexStringLower(hash);
    }

    /// <summary>The normalized content hash of one repository-relative file.</summary>
    public static string HashFile(string relativePath) => NormalizedHash(Read(relativePath));

    /// <summary>
    /// Every file under a repository-relative directory, as repository-relative forward-slash paths,
    /// in ordinal order, excluding build output. An empty extension list selects every file.
    /// </summary>
    public static IReadOnlyList<string> FilesUnder(string relativeDirectory, params string[] extensions)
    {
        var absolute = Path.Combine(
            RepositoryRoot(), relativeDirectory.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(absolute))
        {
            return [];
        }

        return Directory.EnumerateFiles(absolute, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/'))
            .Where(path => !IsBuildOutput(path))
            .Where(path => extensions.Length == 0
                || extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsBuildOutput(string relativePath) =>
        relativePath.Contains("/bin/", StringComparison.Ordinal)
        || relativePath.Contains("/obj/", StringComparison.Ordinal);

    // ================= comment-aware token scanning ======================================

    /// <summary>
    /// The comment ranges of a source text: C# line comments, C# block comments and Razor comments.
    /// A <c>//</c> preceded by a quote or a colon is not treated as a comment start, so a URL inside a
    /// string literal cannot hide code behind a comment mask.
    /// </summary>
    private static readonly Regex CommentRanges = new(
        @"(?<line>(?<![:""'])\/\/[^\r\n]*)|(?<block>/\*.*?\*/)|(?<razor>@\*.*?\*@)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Removes Razor comments so a documentation comment is not mistaken for markup.</summary>
    public static string WithoutRazorComments(string source) =>
        Regex.Replace(source, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline);

    /// <summary>Removes CSS comments so a documentation comment is not mistaken for a rule.</summary>
    public static string WithoutCssComments(string source) =>
        Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    /// <summary>
    /// Every occurrence of a whole token (identifier boundaries: not preceded or followed by a word
    /// character or an underscore) that lies <b>outside</b> a comment — that is, in code, markup,
    /// schema text or payload. The match is case-insensitive.
    /// </summary>
    public static IReadOnlyList<int> CodeOccurrences(string content, string token)
    {
        var mask = CommentMask(content);

        return Regex.Matches(
                content,
                $@"(?<![A-Za-z0-9_]){Regex.Escape(token)}(?![A-Za-z0-9_])",
                RegexOptions.IgnoreCase)
            .Where(match => !mask[match.Index])
            .Select(match => match.Index)
            .ToList();
    }

    /// <summary>
    /// Every fragment occurrence (no identifier boundary) that lies <b>outside</b> a comment. Used for
    /// tokens that must also catch a longer word, such as <c>defini</c>.
    /// </summary>
    public static IReadOnlyList<int> SubstringOccurrences(string content, string token)
    {
        var mask = CommentMask(content);
        var occurrences = new List<int>();

        var index = content.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            if (!mask[index])
            {
                occurrences.Add(index);
            }

            index = content.IndexOf(token, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return occurrences;
    }

    private static bool[] CommentMask(string content)
    {
        var mask = new bool[content.Length];

        foreach (Match match in CommentRanges.Matches(content))
        {
            for (var index = match.Index; index < match.Index + match.Length && index < mask.Length; index++)
            {
                mask[index] = true;
            }
        }

        return mask;
    }

    /// <summary>The P2-T04 production sources whose code/comment/markup mentions the whole token.</summary>
    public static IReadOnlyList<string> ProductionSourcesMentioningInCode(string token) =>
        ProductionSourcePaths
            .Where(path => CodeOccurrences(Read(path), token).Count > 0)
            .ToList();

    /// <summary>The P2-T04 production sources whose code/markup mentions the fragment outside a comment.</summary>
    public static IReadOnlyList<string> ProductionSourcesMentioningFragment(string token) =>
        ProductionSourcePaths
            .Where(path => SubstringOccurrences(Read(path), token).Count > 0)
            .ToList();

    // ================= P2-T04 production sources =========================================

    /// <summary>The new P2-T04 domain sources (contract Appendix B.1).</summary>
    public static IReadOnlyList<string> DomainSourcePaths { get; } =
        FilesUnder("src/DMO.Domain/Tools", ".cs")
            .Concat(FilesUnder("src/DMO.Domain/JobOn", ".cs"))
            .ToList();

    /// <summary>The new P2-T04 application sources (contract Appendix B.2).</summary>
    public static IReadOnlyList<string> ApplicationSourcePaths { get; } =
        FilesUnder("src/DMO.Application/Tools", ".cs")
            .Concat(FilesUnder("src/DMO.Application/JobOn", ".cs"))
            .Concat(
            [
                "src/DMO.Application/Repositories/IToolRepository.cs",
                "src/DMO.Application/Repositories/IJobOnRepository.cs",
                "src/DMO.Application/Persistence/ToolPersistenceException.cs",
                "src/DMO.Application/Persistence/JobOnPersistenceException.cs",
            ])
            .ToList();

    /// <summary>The new P2-T04 persistence sources (contract Appendix B.3).</summary>
    public static IReadOnlyList<string> PersistenceSourcePaths { get; } =
    [
        "src/DMO.Infrastructure/Persistence/ToolJobOn/ToolRepository.cs",
        "src/DMO.Infrastructure/Persistence/ToolJobOn/JobOnRepository.cs",
        "src/DMO.Infrastructure/Persistence/ToolJobOn/JobOnLineageDependencyProbe.cs",
        "src/DMO.Infrastructure/Persistence/ToolJobOn/Entities/ToolEntity.cs",
        "src/DMO.Infrastructure/Persistence/ToolJobOn/Entities/ToolMachineEntity.cs",
        "src/DMO.Infrastructure/Persistence/ToolJobOn/Entities/JobOnEntity.cs",
        "src/DMO.Infrastructure/Persistence/ToolJobOn/Entities/CmContextEntity.cs",
        "src/DMO.Infrastructure/Persistence/ToolJobOn/Entities/MfContextEntity.cs",
        "src/DMO.Infrastructure/Persistence/ToolJobOn/Entities/BqContextEntity.cs",
        "src/DMO.Infrastructure/Persistence/ToolJobOn/EntityConfigurations/ToolEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/ToolJobOn/EntityConfigurations/ToolMachineEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/ToolJobOn/EntityConfigurations/JobOnEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/ToolJobOn/EntityConfigurations/CmContextEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/ToolJobOn/EntityConfigurations/MfContextEntityConfiguration.cs",
        "src/DMO.Infrastructure/Persistence/ToolJobOn/EntityConfigurations/BqContextEntityConfiguration.cs",
    ];

    /// <summary>The single P2-T04 migration and its EF designer (contract §16.1).</summary>
    public static IReadOnlyList<string> MigrationSourcePaths { get; } =
    [
        "src/DMO.Infrastructure/Migrations/20260922232349_ToolJobOnDomainCore.cs",
        "src/DMO.Infrastructure/Migrations/20260922232349_ToolJobOnDomainCore.Designer.cs",
    ];

    /// <summary>
    /// The table names the P2-T04 migration actually creates, read from its own
    /// <c>CreateTable(name: "…")</c> calls, in ordinal order. This is the schema-level proof that no
    /// seventh table — and in particular no machines table — is created.
    /// </summary>
    public static IReadOnlyList<string> CreatedTableNames() =>
        Regex.Matches(
                Read(MigrationSourcePaths[0]),
                @"CreateTable\(\s*name:\s*""(?<name>[A-Za-z0-9_]+)""")
            .Select(match => match.Groups["name"].Value)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>The new P2-T04 Web surfaces (contract Appendix B.4).</summary>
    public static IReadOnlyList<string> WebSourcePaths { get; } =
        FilesUnder("src/DMO.Web/Pages/JobOn", ".cs", ".cshtml")
            .Concat(FilesUnder("src/DMO.Web/Pages/Ferramentas", ".cs", ".cshtml"))
            .Concat(
            [
                "src/DMO.Web/Endpoints/JobOnEndpoints.cs",
                "src/DMO.Web/Endpoints/FerramentasEndpoints.cs",
            ])
            .ToList();

    /// <summary>The two new P2-T04 assets (contract §18.2 rules 5 and 6).</summary>
    public static IReadOnlyList<string> AssetPaths { get; } =
    [
        "src/DMO.Web/wwwroot/css/dmo-jobon.css",
        "src/DMO.Web/wwwroot/js/dmo-jobon.js",
    ];

    /// <summary>Every P2-T04 production source scanned by the vocabulary rows.</summary>
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
    /// The legacy/fake production-identity tokens of contract §17.1 and AC-98. Composed from parts so
    /// this assertion source never carries the literal.
    /// </summary>
    public static IReadOnlyList<string> LegacyIdentityTokens { get; } =
    [
        string.Concat("production", "_id"),
        string.Concat("job", "_on_revision", "_id"),
        string.Concat("production", "Id"),
        string.Concat("jobOn", "Revision", "Id"),
        string.Concat("revision", "Id"),
    ];

    /// <summary>
    /// The repairer / machine-assignment / machine-registry vocabulary of contract §17.1 and AC-99,
    /// matched at identifier boundaries. The machine CODE column and the <c>tool_machines</c> table are
    /// legitimate: a boundary match on <c>machine_id</c> or <c>machines</c> cannot fire inside
    /// <c>tool_machine_id</c>, <c>tool_machines</c> or <c>tool_machines_machine_idx</c>.
    /// </summary>
    public static IReadOnlyList<string> RepairerAndMachineBoundaryTokens { get; } =
    [
        string.Concat("repair", "er"),
        string.Concat("repar", "ador"),
        string.Concat("machine", "_id"),
        string.Concat("machine", "id"),
        string.Concat("machine", "_registry"),
        string.Concat("linha", " ", "b"),
        string.Concat("linha", " ", "c"),
    ];

    /// <summary>
    /// The <b>quoted schema names</b> that would declare a machines table or a <c>machine_id</c>
    /// column (<c>"machines"</c>, <c>"machine_id"</c>, <c>"repairers"</c>,
    /// <c>"machine_registry"</c>). The quoted form is deliberate: the legitimate
    /// <c>"tool_machines"</c> table, its <c>"tool_machine_id"</c> primary key and its
    /// <c>"tool_machines_machine_idx"</c> index are all preceded by an underscore inside their quoted
    /// name, so they can never satisfy a quoted-name match, while a real standalone table or column
    /// declaration always does.
    /// </summary>
    public static IReadOnlyList<string> QuotedSchemaTokens { get; } =
    [
        string.Concat("\"", "machine", "s", "\""),
        string.Concat("\"", "machine", "_id", "\""),
        string.Concat("\"", "repair", "er", "s", "\""),
        string.Concat("\"", "machine", "_registry", "\""),
    ];

    /// <summary>
    /// The fragment tokens that must also catch a longer word: <c>defini</c> (Definições / Definicoes /
    /// definition) and <c>settings</c> (contract §17.1 and AC-99/AC-100).
    /// </summary>
    public static IReadOnlyList<string> DefiniSettingsFragments { get; } =
    [
        string.Concat("defini"),
        string.Concat("sett", "ings"),
    ];

    /// <summary>
    /// The P2-T05-or-later vocabulary of contract §17.4 and AC-100. <c>bq_contexts</c>/BQ are
    /// legitimate and are deliberately absent from this table.
    /// </summary>
    public static IReadOnlyList<string> LaterWorkstreamTokens { get; } =
    [
        string.Concat("Defini", "\u00e7\u00f5es"),
        string.Concat("Defini", "coes"),
        string.Concat("Pe", "so"),
        string.Concat("Compara", "\u00e7\u00e3o"),
        string.Concat("Compara", "cao"),
        string.Concat("Pega", "mentos"),
        string.Concat("Fol", "ha"),
        string.Concat("Resu", "mo"),
        string.Concat("Boqui", "lhas"),
        string.Concat("P", "DF"),
        string.Concat("Hist\u00f3rico", " Global"),
        string.Concat("Historico", " Global"),
        string.Concat("Secondary", "Navigation"),
        string.Concat("SecondaryDestination", "Presentation"),
        string.Concat("Module", "Registrations"),
        string.Concat("CurrentBuild", "Available"),
        string.Concat("DestinationRoute", "Registrations"),
        string.Concat("IDestinationRoute", "Registry"),
        string.Concat("Navigation", "ProjectionService"),
    ];

    /// <summary>
    /// The P2-T04 domain vocabulary used by the changed-path allow-list row (BND9). Composed from
    /// parts, matched case-insensitively as fragments.
    /// </summary>
    public static IReadOnlyList<string> SourceVocabularyFragments { get; } =
    [
        string.Concat("tool", "_"),
        string.Concat("tool", "id"),
        string.Concat("job", "_on"),
        string.Concat("cm", "_contexts"),
        string.Concat("mf", "_contexts"),
        string.Concat("bq", "_contexts"),
        string.Concat("tool", "_machines"),
    ];

    /// <summary>
    /// The <c>jobon</c> fragment for the changed-path allow-list row, excluding the pre-existing
    /// canonical Module member names <c>JobOnView</c>/<c>JobOnCreate</c> that
    /// <c>ModuleCatalog</c> has always declared.
    /// </summary>
    public static string SourceVocabularyJobOnPattern { get; } =
        string.Concat("job", "on", "(?!(?:view|create))");

    /// <summary>
    /// The precise P2-T04 vocabulary used by the test-file rows (BND10–BND12): table names, domain
    /// types and the three contracted registration surfaces. Deliberately excludes the bare
    /// <c>jobon</c>/<c>job_on</c>/<c>tool</c> fragments that the accepted P2-T03 scan helpers carry.
    /// </summary>
    public static IReadOnlyList<string> TestVocabulary { get; } =
    [
        string.Concat("tool", "_machines"),
        string.Concat("cm", "_contexts"),
        string.Concat("mf", "_contexts"),
        string.Concat("bq", "_contexts"),
        string.Concat("job", "_ons"),
        string.Concat("Job", "On", "Entity"),
        string.Concat("Tool", "Entity"),
        string.Concat("Tool", "MachineEntity"),
        string.Concat("Job", "On", "Id"),
        string.Concat("Tool", "Id"),
        string.Concat("Job", "On", "Repository"),
        string.Concat("Tool", "Repository"),
        string.Concat("I", "Job", "On", "Repository"),
        string.Concat("I", "Tool", "Repository"),
        string.Concat("I", "Job", "On", "Service"),
        string.Concat("I", "Tool", "Service"),
        string.Concat("Job", "On", "Service"),
        string.Concat("Tool", "Service"),
        string.Concat("Job", "On", "Models"),
        string.Concat("Job", "On", "Validator"),
        string.Concat("Tool", "Models"),
        string.Concat("Tool", "Validator"),
        string.Concat("Tool", "Compatibility"),
        string.Concat("Tool", "ContextType"),
        string.Concat("Tool", "ContextSnapshot"),
        string.Concat("Machine", "Code"),
        string.Concat("Job", "On", "ToolPickerAdapter"),
        string.Concat("Job", "On", "PolicyNames"),
        string.Concat("Job", "On", "LineageDependencyProbe"),
        string.Concat("I", "Job", "On", "DependencyProbe"),
        string.Concat("Map", "Job", "On", "Endpoints"),
        string.Concat("Map", "Ferramentas", "Endpoints"),
        string.Concat("DMO.Domain.", "Job", "On"),
        string.Concat("DMO.Application.", "Job", "On"),
        string.Concat("DMO.Domain.", "Tools"),
        string.Concat("DMO.Application.", "Tools"),
    ];

    // ================= ownership / allow-list ============================================

    /// <summary>The P2-T04 owned source paths (contract Appendix B and §13.6).</summary>
    public static IReadOnlyList<string> OwnedPathPrefixes { get; } =
    [
        "src/DMO.Domain/Tools/",
        "src/DMO.Domain/JobOn/",
        "src/DMO.Application/Tools/",
        "src/DMO.Application/JobOn/",
        "src/DMO.Application/Repositories/",
        "src/DMO.Application/Persistence/",
        "src/DMO.Infrastructure/Persistence/",
        "src/DMO.Infrastructure/Migrations/",
        "src/DMO.Web/Pages/JobOn/",
        "src/DMO.Web/Pages/Ferramentas/",
        "src/DMO.Web/Endpoints/",
        "src/DMO.Web/wwwroot/css/dmo-jobon.css",
        "src/DMO.Web/wwwroot/js/dmo-jobon.js",
    ];

    /// <summary>
    /// The only accepted non-new files P2-T04 changes (contract §17.2, last paragraph).
    /// <c>DmoDbContext.cs</c> is listed because it is the protected file the allow-list must not
    /// excuse: the row asserts separately that it carries no P2-T04 vocabulary at all.
    /// </summary>
    public static IReadOnlyList<string> DocumentedAdditiveSourcePaths { get; } =
    [
        "src/DMO.Web/Program.cs",
        "src/DMO.Infrastructure/Persistence/Core/DmoDbContext.cs",
        "src/DMO.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs",
        "src/DMO.Infrastructure/Migrations/DmoDbContextModelSnapshot.cs",
    ];

    /// <summary>The protected foundation file that must stay free of P2-T04 vocabulary.</summary>
    public const string ProtectedDbContextPath = "src/DMO.Infrastructure/Persistence/Core/DmoDbContext.cs";

    /// <summary>Every <c>src</c> file that mentions the P2-T04 vocabulary outside the allow-list.</summary>
    public static IReadOnlyList<string> VocabularyMentionsOutsideOwnedPaths()
    {
        var offenders = new List<string>();

        foreach (var path in FilesUnder("src", ".cs", ".cshtml", ".css", ".js", ".json", ".razor"))
        {
            var content = Read(path);

            var mentions =
                SourceVocabularyFragments.Any(fragment =>
                    content.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                || Regex.IsMatch(content, SourceVocabularyJobOnPattern, RegexOptions.IgnoreCase);

            if (!mentions)
            {
                continue;
            }

            if (OwnedPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)))
            {
                continue;
            }

            if (DocumentedAdditiveSourcePaths.Contains(path, StringComparer.Ordinal))
            {
                continue;
            }

            offenders.Add(path);
        }

        return offenders;
    }

    // ================= test-file scanning (BND10–BND12) =================================

    /// <summary>
    /// The new P2-T04 test folders excluded from the "existing test" scans, plus the P2-T05 new
    /// test folders (disclosed P2-T05 extension, P2-T05 response): P2-T05's own test surface is
    /// excluded here exactly like P2-T04's own was, so the P2-T04 "existing tests never carry
    /// vocabulary" rows keep scanning only the pre-existing surface.
    /// </summary>
    public static IReadOnlyList<string> NewP2T04TestFolders { get; } =
    [
        "tests/DMO.UnitTests/Tools/",
        "tests/DMO.UnitTests/JobOn/",
        "tests/DMO.IntegrationTests/Tools/",
        "tests/DMO.IntegrationTests/JobOn/",
        // ---- P2-T05 (disclosed): the new Controlo test surface -----------------------------
        "tests/DMO.UnitTests/ControloCreate/",
        "tests/DMO.IntegrationTests/ControloCreate/",
        // ---- P2-T06 (disclosed): the new Controlo Approve test surface --------------------
        "tests/DMO.UnitTests/ControloApprove/",
        "tests/DMO.IntegrationTests/ControloApprove/",
        // ---- P2-T07 (disclosed): the new Boquilhas test surface ---------------------------
        "tests/DMO.UnitTests/Boquilhas/",
        "tests/DMO.IntegrationTests/Boquilhas/",
        // ---- Peso Comparacao slice (disclosed): the new ControloComparacao unit-test surface --
        // (the integration-side Comparacao persistence tests are disclosed by file name in
        // NewP2T04PersistenceTestFiles below)
        "tests/DMO.UnitTests/ControloComparacao/",
    ];

    /// <summary>
    /// The one pre-existing test file this workstream modified, with the disclosure recorded in the
    /// file itself (contract §3/§16.5: the six domain-core tables are mapped by the same single
    /// <c>DmoDbContext</c>, so the exact modelled-table assertion necessarily grew by six names). It is
    /// listed here explicitly so the BND12 row fails if any <i>other</i> existing test file starts
    /// carrying P2-T04 vocabulary; the modification itself is reported as a deviation, not hidden.
    /// </summary>
    public static IReadOnlyList<string> DisclosedAdditiveTestPaths { get; } =
    [
        "tests/DMO.IntegrationTests/MigrationRunnerTests.cs",
        "tests/DMO.IntegrationTests/DatabaseConnectivityTests.cs",
        // ---- P2-T06 (disclosed): the pre-existing test files this phase extended ----------
        // The modelled-table/migration inventory rows necessarily grow by the one decision table
        // and the sixth migration; the Tool DbSet-restriction inventory discloses the one review
        // entity join; the P2-T05 access row discloses the now-existing approve surface; the
        // P2-T05 test store gains the P2-T06 arrangement surface. All are additive disclosures of
        // the same pins — never weakenings (each change is documented in-file).
        "tests/DMO.UnitTests/Tools/ToolRestrictionTests.cs",
        "tests/DMO.IntegrationTests/ControloCreate/ControloCreateAccessTests.cs",
        "tests/DMO.IntegrationTests/ControloCreate/P2T05TestStore.cs",
        "tests/DMO.IntegrationTests/Persistence/Migration003ToolJobOnDomainCoreTests.cs",
        "tests/DMO.IntegrationTests/Persistence/Migration004ControloCreateDomainTests.cs",
        "tests/DMO.IntegrationTests/Persistence/Migration005GlassDensitySettingsTests.cs",
        // ---- P2-T07 (disclosed): the pre-existing files the OWNER CLARIFICATION correction
        // extended ----------------------------------------------------------------
        // The Job On test store gains ONE additive read-only arrangement member
        // (<c>ContextsOf</c>): the Boquilhas test composition resolves contexts created through
        // the REAL association flow from the Job On store's single source of truth instead of
        // duplicating them in a mirror. The inventory rows (Migration003/004/005/006/
        // MigrationRunner/DatabaseConnectivity) grow to the corrected THREE-table final schema
        // (23 product tables; the unreviewed 007 lifecycle tables are gone).
        "tests/DMO.IntegrationTests/JobOn/P2T04TestStore.cs",
        "tests/DMO.IntegrationTests/Persistence/Migration006ControloApproveDomainTests.cs",
        // ---- P2-T08 (disclosed repair, outputs slice): the pre-existing documents test files the
        // P2-T08 slices created under tests/DMO.UnitTests/Documents ---------------------------
        // The P2-T08 email group-routing and Peso PDF generation slices added their own test
        // surface under tests/DMO.UnitTests/Documents without extending this disclosure; the
        // files legitimately carry the P2-T04 vocabulary (the canonical Tool identity tuple of
        // the SHARED sheet fixtures and the canonical MachineCode of the email machine-group
        // routing). The boundary row was red since those slices; this disclosure repairs the
        // omission — an additive repair, never a weakening.
        "tests/DMO.UnitTests/Documents/EmailMachineGroupTests.cs",
        "tests/DMO.UnitTests/Documents/PesoPdfFixtures.cs",
    ];

    /// <summary>The new P2-T04 env-gated persistence test files (contract §20.9/§20.10).</summary>
    public static IReadOnlyList<string> NewP2T04PersistenceTestFiles { get; } =
    [
        "ToolRepositoryIntegrationTests.cs",
        "JobOnRepositoryIntegrationTests.cs",
        "JobOnDuplicationIntegrationTests.cs",
        "JobOnDeleteDependencyIntegrationTests.cs",
        "Migration003ToolJobOnDomainCoreTests.cs",
        // Architect-mandated save-time concurrency-race regression (implementation review
        // 5d490113b8dd1d80759742cc86a99e8f0294b44c, §15.1 correction): a new P2-T04 env-gated
        // persistence test, deliberately outside the 155-row contract matrix (see the response
        // file correction section).
        "JobOnSaveTimeConcurrencyTests.cs",
        // ---- P2-T05 (disclosed): the new env-gated Controlo persistence tests --------------
        "PesoRepositoryIntegrationTests.cs",
        "ControloSettingsRepositoryIntegrationTests.cs",
        "Migration004ControloCreateDomainTests.cs",
        "PesoJobOnDependencyProbeIntegrationTests.cs",
        // ---- P2-T05 post-closure glass-density correction (disclosed): the new env-gated
        // persistence tests of the correction slice ---------------------------------------
        "Migration005GlassDensitySettingsTests.cs",
        "GlassDensitySettingsRepositoryIntegrationTests.cs",
        // ---- P2-T06 (disclosed): the new env-gated Controlo Approve persistence tests ---------
        "Migration006ControloApproveDomainTests.cs",
        "PesoReviewRepositoryIntegrationTests.cs",
        // ---- P2-T07 (disclosed): the new env-gated Boquilhas persistence tests --------------
        "Migration007BoquilhasDomainTests.cs",
        "BoquilhasRepositoryIntegrationTests.cs",
        // ---- Owner-clarification Resumo/association delta (disclosed): the new env-gated
        // persistence tests of the Controlo entry + pré-JobOn association slice -------------
        "PesoResumoAssociationIntegrationTests.cs",
        // ---- Owner-clarification Boquilhas §34 delta (disclosed): the new env-gated
        // persistence tests of the pré-JobOn register + association slice --------------------
        "BoquilhasPreJobonAssociationIntegrationTests.cs",
        // ---- Peso Comparacao slice (disclosed): the new env-gated Comparacao persistence
        // tests (repository + migration rows, same skip gate as every DB-class row) ----------
        "ComparacaoRepositoryIntegrationTests.cs",
        "Migration009ControloComparacaoDomainTests.cs",
    ];

    /// <summary>Returns whether a repository-relative test path belongs to the new P2-T04 surface.</summary>
    public static bool IsNewP2T04TestPath(string relativePath) =>
        NewP2T04TestFolders.Any(folder => relativePath.StartsWith(folder, StringComparison.Ordinal))
        || Path.GetFileName(relativePath).StartsWith("P2T04", StringComparison.OrdinalIgnoreCase)
        || NewP2T04PersistenceTestFiles.Contains(
            Path.GetFileName(relativePath), StringComparer.Ordinal);

    /// <summary>Every pre-existing test source file under a test project directory.</summary>
    public static IReadOnlyList<string> PreExistingTestFiles(
        string projectDirectory,
        params string[] extensions) =>
        FilesUnder(projectDirectory, extensions)
            .Where(path => !IsNewP2T04TestPath(path))
            .ToList();

    /// <summary>Every pre-existing test source file whose text mentions P2-T04 vocabulary.</summary>
    public static IReadOnlyList<string> TestVocabularyOffenders(
        string projectDirectory,
        params string[] extensions) =>
        PreExistingTestFiles(projectDirectory, extensions)
            .Where(path => TestVocabulary.Any(token =>
                Read(path).Contains(token, StringComparison.Ordinal)))
            .ToList();

    /// <summary>
    /// The attribute lines declared directly above the enclosing member of the source line at
    /// <paramref name="callLineIndex"/>. Used to prove that a DB-class test call site is gated by
    /// <c>[SkippableFact]</c> (or its theory form <c>[SkippableTheory]</c>).
    /// </summary>
    public static IReadOnlyList<string> AttributesOfEnclosingMember(string[] lines, int callLineIndex)
    {
        var declaration = -1;

        for (var index = callLineIndex - 1; index >= 0; index--)
        {
            if (Regex.IsMatch(lines[index].Trim(), @"^(public|private|internal|protected)\b"))
            {
                declaration = index;
                break;
            }
        }

        if (declaration < 0)
        {
            return [];
        }

        var attributes = new List<string>();

        for (var index = declaration - 1; index >= 0; index--)
        {
            var line = lines[index].Trim();

            if (line.StartsWith('['))
            {
                attributes.Add(line);
                continue;
            }

            if (line.Length == 0)
            {
                continue;
            }

            break;
        }

        return attributes;
    }

    /// <summary>The call-site marker of the environment gate.</summary>
    public const string PersistenceGateCall = "PersistenceTestDatabase.SkipIfNotConfigured()";

    /// <summary>
    /// Every environment-gated call site whose enclosing member is not marked with a skippable
    /// attribute, as an <c>file : line : attributes</c> description.
    /// </summary>
    public static IReadOnlyList<string> UngatedPersistenceCallSites(string projectDirectory)
    {
        var offenders = new List<string>();

        foreach (var path in PreExistingTestFiles(projectDirectory, ".cs"))
        {
            var lines = File.ReadAllLines(Path.Combine(
                RepositoryRoot(), path.Replace('/', Path.DirectorySeparatorChar)));

            for (var index = 0; index < lines.Length; index++)
            {
                if (lines[index].IndexOf(PersistenceGateCall, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                var attributes = AttributesOfEnclosingMember(lines, index);

                if (attributes.Any(attribute =>
                        attribute.StartsWith("[SkippableFact]", StringComparison.Ordinal)
                        || attribute.StartsWith("[SkippableTheory", StringComparison.Ordinal)))
                {
                    continue;
                }

                offenders.Add(
                    $"{path} : line {index + 1} : [{string.Join("; ", attributes)}]");
            }
        }

        return offenders;
    }
}
