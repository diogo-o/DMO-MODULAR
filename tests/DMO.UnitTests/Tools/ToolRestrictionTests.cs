using System.Reflection;
using System.Text.RegularExpressions;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Web.Endpoints.ToolJobOn;

namespace DMO.UnitTests.Tools;

/// <summary>
/// P2-T04 restriction unit — the identity/registry restrictions of the Tool area proved by reflection
/// and by source scan: the transport surface carries only the canonical <c>toolId</c>, no second Tool
/// registry or per-surface Tool cache exists, the service exposes no update/delete operation, and the
/// Tool create transaction is the only producer of a canonical Tool identity.
/// <para>
/// Authority: <c>plans/contracts/P2-T04_DOMAIN_CORE_TOOL_JOBON_CONTRACT.md</c> §20.1 rows TOL16,
/// TOL17, TOL18 and TOL20 and §22 AC-1, AC-2, AC-8, AC-12, AC-98.
/// </para>
/// <para>
/// Preconditions: the repository working tree is available relative to <c>DMO.slnx</c>; the
/// reflection part needs no host, no database and no HTTP pipeline.
/// Required non-effects: no provisional/temporary/display-key/negative identity anywhere on the
/// transport surface, no second Tool table/entity/store/cache, no update or delete verb, and no
/// canonical identity minted outside the create transaction.
/// </para>
/// </summary>
public sealed class ToolRestrictionTests
{
    /// <summary>The forbidden identity-implying tokens of the P2-T04 transport surface (TOL16).</summary>
    private static readonly string[] ForbiddenIdentityTokens =
        ["temp", "temporary", "provisional", "display", "draft", "fake", "surrogate", "negative", "clientkey", "clientid"];

    /// <summary>The forbidden second-registry tokens of the Tool persistence surface (TOL17).</summary>
    private static readonly string[] ForbiddenRegistryTokens =
        ["cache", "store", "registry", "draft", "buffer", "staging", "session"];

    /// <summary>The forbidden lifecycle tokens of the Tool service surface (TOL18).</summary>
    private static readonly string[] ForbiddenMutationTokens =
        ["update", "delete", "remove", "put", "patch", "archive", "deactivate"];

    /// <summary>
    /// TOL16 (AC-8, AC-98) — no P2-T04 transport/response type or produced body carries a
    /// provisional, temporary, negative, display-key or client-supplied Tool identity: the only Tool
    /// identity member is the canonical <c>toolId</c>/<c>ToolId</c> as a <see cref="Guid"/>, and the
    /// create request carries no identity member at all.
    /// </summary>
    [Fact]
    public void TOL16_TransportSurfaceCarriesOnlyTheCanonicalToolIdIdentity()
    {
        var transportTypes = typeof(FerramentasEndpoints)
            .GetNestedTypes(BindingFlags.Public)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(transportTypes);
        Assert.Contains(typeof(FerramentasEndpoints.ToolCreatedResponse), transportTypes);

        var properties = transportTypes
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(property => (Type: type, Property: property)))
            .ToList();

        // Exactly these identity members exist — every Tool identity is the canonical ToolId Guid,
        // and the single other identity is the Job On occurrence the ficha lists.
        var identityMembers = properties
            .Where(entry => entry.Property.Name.EndsWith("Id", StringComparison.Ordinal))
            .Select(entry => $"{entry.Type.Name}.{entry.Property.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "ToolCreatedResponse.ToolId",
                "ToolFichaResponse.ToolId",
                "ToolSearchItemResponse.ToolId",
                "ToolUsageResponse.JobonId",
            ],
            identityMembers);

        Assert.All(
            properties.Where(entry => entry.Property.Name.EndsWith("Id", StringComparison.Ordinal)),
            entry => Assert.Equal(typeof(Guid), entry.Property.PropertyType));

        // No identity member is nullable, negative-capable or client-minted.
        Assert.All(
            properties.Where(entry => entry.Property.Name.EndsWith("Id", StringComparison.Ordinal)),
            entry => Assert.False(
                Nullable.GetUnderlyingType(entry.Property.PropertyType) is not null,
                $"{entry.Type.Name}.{entry.Property.Name} is a provisional/optional identity."));

        // The create request supplies the six Tool facts and NO identity: the client cannot name one.
        var createRequest = typeof(FerramentasEndpoints.CreateToolRequest);

        Assert.Equal(
            ["Lot", "Machines", "Processo", "Quantity", "Reference", "Type"],
            createRequest
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.DoesNotContain(
            createRequest.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.Name.Contains("Id", StringComparison.Ordinal));

        // No transport member names a provisional, temporary, display-key, fake or negative identity.
        Assert.All(properties, entry => Assert.All(
            ForbiddenIdentityTokens,
            token => Assert.DoesNotContain(
                token, entry.Property.Name, StringComparison.OrdinalIgnoreCase)));

        // The produced bodies are built from code identifiers only; the sole identity identifiers are
        // the canonical ones, and no forbidden identity token appears as an identifier anywhere.
        var endpointSource = CodeOnly(SourceOf("src/DMO.Web/Endpoints/ToolJobOn/FerramentasEndpoints.cs"));
        var endpointIdentifiers = Identifiers(endpointSource);

        Assert.Equal(
            ["JobOnId", "JobonId", "ToolId", "existingToolId", "toolId"],
            endpointIdentifiers
                .Where(identifier => identifier.EndsWith("Id", StringComparison.Ordinal) && identifier != "Guid")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(identifier => identifier, StringComparer.Ordinal));

        Assert.All(endpointIdentifiers, identifier => Assert.All(
            ForbiddenIdentityTokens,
            token => Assert.DoesNotContain(token, identifier, StringComparison.OrdinalIgnoreCase)));

        // The canonical identity travels as the real Guid the service allocated.
        Assert.Equal(
            typeof(Guid),
            typeof(FerramentasEndpoints.ToolCreatedResponse)
                .GetProperty(nameof(FerramentasEndpoints.ToolCreatedResponse.ToolId))!.PropertyType);
    }

    /// <summary>
    /// TOL17 (AC-1) — no second Tool registry exists: exactly one Tool registry entity/configuration
    /// maps the one <c>tools</c> table, the only other Tool-named persistence type is the contracted
    /// compatibility row, no Tool table, <c>DbSet</c> or store/cache type exists anywhere in
    /// <c>src</c>.
    /// </summary>
    [Fact]
    public void TOL17_NoSecondToolRegistryEntityTableDbSetOrCacheExists()
    {
        var entities = SourcesUnder("src/DMO.Infrastructure/Persistence/Access/Entities")
            .Concat(SourcesUnder("src/DMO.Infrastructure/Persistence/ToolJobOn/Entities"))
            .Concat(SourcesUnder("src/DMO.Infrastructure/Persistence/Controlo/Entities"))
            .Concat(SourcesUnder("src/DMO.Infrastructure/Persistence/Boquilhas/Entities"))
            .ToList();
        var configurations = SourcesUnder("src/DMO.Infrastructure/Persistence/Access/EntityConfigurations")
            .Concat(SourcesUnder("src/DMO.Infrastructure/Persistence/ToolJobOn/EntityConfigurations"))
            .Concat(SourcesUnder("src/DMO.Infrastructure/Persistence/Controlo/EntityConfigurations"))
            .Concat(SourcesUnder("src/DMO.Infrastructure/Persistence/Boquilhas/EntityConfigurations"))
            .ToList();
        var infrastructure = SourcesUnder("src/DMO.Infrastructure");
        var application = SourcesUnder("src/DMO.Application");
        var allSources = SourcesUnder("src");

        // (a) The Tool-named persistence entities: the registry row and its compatibility rows only.
        Assert.Equal(
            ["ToolEntity", "ToolMachineEntity"],
            MatchGroup(entities, @"\bclass\s+(\w+)\b")
                .Where(name => name.Contains("Tool", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal));

        // Exactly ONE of them is the registry: it owns the identity tuple (reference + lot).
        var registryEntities = entities
            .Where(source => Regex.IsMatch(source.Text, @"\bstring\s+Reference\b") &&
                             Regex.IsMatch(source.Text, @"\bstring\s+Lot\b"))
            .Select(source => Path.GetFileName(source.Path))
            .ToList();

        Assert.Equal(["ToolEntity.cs"], registryEntities);

        // (b) Exactly one configuration maps the one canonical registry table, and the Tool-named
        // configurations are exactly the registry and the contracted compatibility table.
        Assert.Equal(
            ["ToolEntityConfiguration.cs"],
            configurations
                .Where(source => source.Literals.Contains("ToTable(\"tools\"", StringComparison.Ordinal))
                .Select(source => Path.GetFileName(source.Path))
                .ToList());

        Assert.Equal(
            ["ToolEntityConfiguration", "ToolMachineEntityConfiguration"],
            configurations
                .Where(source => Path.GetFileName(source.Path).Contains("Tool", StringComparison.Ordinal))
                .Select(source => Path.GetFileNameWithoutExtension(source.Path))
                .OrderBy(name => name, StringComparer.Ordinal));

        Assert.Equal(
            ["ToolEntity"],
            MatchGroup(
                configurations.Where(source =>
                    Path.GetFileNameWithoutExtension(source.Path) == "ToolEntityConfiguration"),
                @"IEntityTypeConfiguration<\s*(\w+)\s*>"));
        Assert.Equal(
            ["ToolMachineEntity"],
            MatchGroup(
                configurations.Where(source =>
                    Path.GetFileNameWithoutExtension(source.Path) == "ToolMachineEntityConfiguration"),
                @"IEntityTypeConfiguration<\s*(\w+)\s*>"));

        // (c) Across the whole of src there is no third Tool table: the tool-ish table names are
        // exactly the registry and its contracted compatibility table.
        var tableNames = MatchGroup(allSources, @"ToTable\(\s*""([a-z0-9_]+)""", withLiterals: true)
            .Concat(MatchGroup(allSources, @"CreateTable\(\s*name:\s*""([a-z0-9_]+)""", withLiterals: true))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(tableNames);
        Assert.Contains("tools", tableNames);
        Assert.Contains("job_ons", tableNames);
        Assert.Equal(
            ["tool_machines", "tools"],
            tableNames
                .Where(name => name.Contains("tool", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal));

        // (d) No DbSet is declared over a Tool entity, and the declared DbSet set is the pre-P2-T04
        // one. Since P2-T05 (disclosed): the eight Controlo entities join the set — the P2-T05
        // repositories obtain their sets through the same private `DbSet<TEntity> => _context.Set<T>()`
        // convention, and the "no Tool entity is ever a DbSet" rule is unchanged. Since the
        // post-closure glass-density correction (disclosed): the one approved settings entity
        // joins the set with the same convention. Since P2-T06 (disclosed): exactly ONE review
        // entity joins the set (<c>PesoReviewDecisionEntity</c>); the review repository's composed
        // list reads use the <c>IQueryable</c> accessor convention (no new Tool/CM/JobOn surface).
        // P2-T07 (disclosed in the implementation response) declares NO DbSet member anywhere: the
        // Boquilhas repository uses the same <c>IQueryable</c> accessor convention for reads and
        // <c>_context.Set&lt;TEntity&gt;()</c> inline for its writes, so this inventory is unchanged.
        // Peso Comparação (disclosed in the implementation response): the three Comparação entities
        // join the set with the same private-DbSet convention (the Comparação aggregate lives in
        // its OWN three tables); none carries Tool vocabulary.
        var dbSetArguments = MatchGroup(allSources, @"DbSet<\s*(\w+)\s*>")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "AdminAccountEntity", "ComparacaoCmSubjectEntity", "ComparacaoEntity",
                "ComparacaoMeasurementRowEntity", "EmailListEntity", "EmailListRecipientEntity",
                "EmailTemplateEntity", "GlassDensitySettingEntity", "JobOnEntity",
                "MachineRepairerAssignmentEntity", "PdfDirectorySettingsEntity", "PesoEntity",
                "PesoMeasurementRowEntity", "PesoReviewDecisionEntity", "RepairerEntity",
                "TemplateEntity", "TemplateModuleEntity", "UserEntity",
            ],
            dbSetArguments);
        Assert.DoesNotContain(dbSetArguments, name => name.Contains("Tool", StringComparison.Ordinal));

        // (e) No per-surface Tool store or cache type/member exists in the two Tool-owning projects.
        var registryLikeIdentifiers = infrastructure
            .Concat(application)
            .SelectMany(source => Identifiers(source.Text))
            .Where(identifier => ContainsToolAndRegistryToken(identifier))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(registryLikeIdentifiers);

        // No Tool-typed dictionary or cache collection is held anywhere in the two projects either.
        var toolDictionaries = infrastructure
            .Concat(application)
            .SelectMany(source => MatchGroupFrom(
                source.Text, @"(?:Concurrent)?Dictionary<[^>]*\b(\w*Tool\w*)[^>]*>"))

            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(toolDictionaries);
        Assert.Contains(
            application,
            source => source.Text.Contains("Dictionary<", StringComparison.Ordinal));

        // ... and no Tool cache/store/draft file exists anywhere in src. The Tool-named file set is
        // asserted positive first, so the negative scan is proven to be looking at real Tool sources.
        var toolNamedFiles = allSources
            .Select(source => Path.GetFileNameWithoutExtension(source.Path))
            .Where(name => name.Contains("Tool", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Contains("ToolEntity", toolNamedFiles);
        Assert.Contains("ToolService", toolNamedFiles);
        Assert.Contains("JobOnToolPickerAdapter", toolNamedFiles);
        Assert.DoesNotContain(
            toolNamedFiles,
            name => ForbiddenRegistryTokens.Any(token =>
                name.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// TOL18 (AC-12) — the Tool service surface exposes exactly the three contracted operations and
    /// no update and no delete operation: <c>IToolService</c> declares <c>SearchAsync</c>,
    /// <c>GetAsync</c> and <c>CreateAsync</c>, the repository contract adds none, and the mapped
    /// Ferramentas endpoints use no mutating verb other than the contracted create <c>POST</c>.
    /// </summary>
    [Fact]
    public void TOL18_ToolServiceExposesNoUpdateAndNoDeleteOperation()
    {
        var serviceMembers = typeof(IToolService).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Equal(
            ["CreateAsync", "GetAsync", "SearchAsync"],
            serviceMembers.Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(3, serviceMembers.Length);
        Assert.All(serviceMembers, method => Assert.Equal(typeof(Task<ToolResult>), method.ReturnType));

        // The contracted parameter shapes, so no hidden update/delete overload can hide next to them.
        Assert.Equal(
            [typeof(ToolSearchQuery), typeof(CancellationToken)],
            typeof(IToolService).GetMethod(nameof(IToolService.SearchAsync))!
                .GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            [typeof(Guid), typeof(CancellationToken)],
            typeof(IToolService).GetMethod(nameof(IToolService.GetAsync))!
                .GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            [typeof(CreateToolCommand), typeof(CancellationToken)],
            typeof(IToolService).GetMethod(nameof(IToolService.CreateAsync))!
                .GetParameters().Select(parameter => parameter.ParameterType));

        // No update/delete member on any Tool service, repository or result carrier.
        var toolSurfaceTypes = new[]
        {
            typeof(IToolService),
            typeof(ToolService),
            typeof(IToolRepository),
            typeof(ToolValidator),
            typeof(ToolResult),
            typeof(FerramentasEndpoints),
        };

        var memberNames = toolSurfaceTypes
            .SelectMany(type => type
                .GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(member => $"{type.Name}.{member.Name}")
                .Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(nested => $"{type.Name}.{nested.Name}")))
            .ToList();

        Assert.NotEmpty(memberNames);
        Assert.Contains(memberNames, name => name.EndsWith(".CreateAsync", StringComparison.Ordinal));
        Assert.All(memberNames, name => Assert.All(
            ForbiddenMutationTokens,
            token => Assert.DoesNotContain(token, name, StringComparison.OrdinalIgnoreCase)));

        // No Type-Name revision/version carrier hides an update path either.
        var toolAssemblies = new[] { typeof(IToolService).Assembly, typeof(FerramentasEndpoints).Assembly };
        var updateTypes = toolAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Name.Contains("Tool", StringComparison.Ordinal))
            .Where(type => ForbiddenMutationTokens.Any(token =>
                type.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Select(type => type.FullName!)
            .ToList();

        Assert.Empty(updateTypes);

        // The mapped Tool endpoints use exactly one read verb and the contracted create verb; there is
        // no PUT/PATCH/DELETE route metadata and exactly one mapping entry point.
        var endpointSource = CodeOnly(SourceOf("src/DMO.Web/Endpoints/ToolJobOn/FerramentasEndpoints.cs"));
        var verbs = MatchGroupFrom(endpointSource, @"\bMap(Get|Post|Put|Patch|Delete)\s*\(")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(verb => verb, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["Get", "Post"], verbs);
        Assert.Equal(
            ["Get", "Post"],
            MatchGroupFrom(endpointSource, @"\bMap(Get|Post|Put|Patch|Delete)\s*\(")
                .OrderBy(verb => verb, StringComparer.Ordinal));
        Assert.Equal(
            ["MapFerramentasEndpoints"],
            typeof(FerramentasEndpoints)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(method => method.Name));
        Assert.Equal(
            ["MapFerramentasEndpoints"],
            typeof(FerramentasEndpoints)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => method.ReturnType.Name == "WebApplication")
                .Select(method => method.Name));
    }

    /// <summary>
    /// TOL20 (AC-2) — no Web/application code constructs a canonical Tool identity: the only
    /// <c>ToolId.New()</c>/<c>Guid.NewGuid()</c> producer of a Tool identity in the whole of <c>src</c>
    /// is the Tool create transaction in <c>ToolService.CreateAsync</c>.
    /// </summary>
    [Fact]
    public void TOL20_OnlyTheToolCreateTransactionProducesACanonicalToolIdentity()
    {
        var inScope = SourcesUnder("src/DMO.Application/Tools")
            .Concat(SourcesUnder("src/DMO.Web/Pages/JobOn"))
            .Concat(SourcesUnder("src/DMO.Web/Endpoints"))
            .ToList();

        Assert.NotEmpty(inScope);

        var producers = inScope
            .Where(source => source.Text.Contains("ToolId.New(", StringComparison.Ordinal) ||
                             source.Text.Contains("Guid.NewGuid(", StringComparison.Ordinal))
            .Select(source => Path.GetRelativePath(RepositoryRoot(), source.Path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["src/DMO.Application/Tools/ToolService.cs"], producers);

        // The single production sits inside CreateAsync — not in the search path and not in the read
        // path — so a Tool identity exists only once the create transaction runs.
        var toolService = CodeOnly(SourceOf("src/DMO.Application/Tools/ToolService.cs"));
        var createIndex = toolService.IndexOf("CreateAsync(", StringComparison.Ordinal);
        var searchIndex = toolService.IndexOf("SearchAsync(", StringComparison.Ordinal);
        var getIndex = toolService.IndexOf("GetAsync(", StringComparison.Ordinal);

        Assert.True(searchIndex > 0 && getIndex > 0 && createIndex > getIndex && createIndex > searchIndex);
        Assert.DoesNotContain("ToolId.New(", toolService[..createIndex]);
        Assert.DoesNotContain("Guid.NewGuid(", toolService[..createIndex]);

        var createRegion = toolService[createIndex..toolService.IndexOf(
            "private static ToolSearchItem ToItem", StringComparison.Ordinal)];

        Assert.Contains("ToolId.New(", createRegion);
        Assert.Single(Regex.Matches(createRegion, Regex.Escape("ToolId.New(")).Cast<Match>());

        // Nowhere in src does a second Tool-identity producer exist, and the Web surfaces never even
        // wrap or mint one.
        var wholeSourceProducers = SourcesUnder("src")
            .Where(source => source.Text.Contains("ToolId.New(", StringComparison.Ordinal))
            .Select(source => Path.GetRelativePath(RepositoryRoot(), source.Path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["src/DMO.Application/Tools/ToolService.cs"], wholeSourceProducers);

        var webIdentityConstruction = SourcesUnder("src/DMO.Web/Pages/JobOn")
            .Concat(SourcesUnder("src/DMO.Web/Endpoints"))
            .SelectMany(source => new[] { "ToolId.New(", "ToolId.From(", "new ToolId(" }
                .Where(fragment => source.Text.Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetFileName(source.Path)}: {fragment}"))
            .ToList();

        Assert.Empty(webIdentityConstruction);
    }

    /// <summary>
    /// One repository source file: its comment-free, literal-free text for identifier scans, and its
    /// comment-free text with the literals kept for the table/shape scans.
    /// </summary>
    private sealed record SourceFile(string Path, string Text, string Literals);

    /// <summary>Every <c>.cs</c> source under a repository-relative directory, excluding build output.</summary>
    private static IReadOnlyList<SourceFile> SourcesUnder(string relativeDirectory)
    {
        var directory = Path.Combine(RepositoryRoot(), relativeDirectory.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(Directory.Exists(directory), $"'{relativeDirectory}' does not exist.");

        return Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path =>
            {
                var source = File.ReadAllText(path);
                return new SourceFile(path, CodeOnly(source), WithoutComments(source));
            })
            .ToList();
    }

    /// <summary>The code-only text of one repository-relative source file.</summary>
    private static string SourceOf(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Removes every comment, keeping the string literals (for table/shape scans).</summary>
    private static string WithoutComments(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(withoutBlocks, @"//[^\n]*", " ");
    }

    /// <summary>Removes every comment and string literal, so only real identifiers are scanned.</summary>
    private static string CodeOnly(string source)
    {
        var withoutVerbatim = Regex.Replace(WithoutComments(source), "@\"(?:[^\"]|\"\")*\"", "\"\"");
        return Regex.Replace(withoutVerbatim, "\"(?:[^\"\\\\\\n]|\\\\.)*\"", "\"\"");
    }

    /// <summary>Every identifier token of code-only source text.</summary>
    private static IEnumerable<string> Identifiers(string codeOnlyText) =>
        Regex.Matches(codeOnlyText, @"\b[A-Za-z_][A-Za-z0-9_]*\b").Select(match => match.Value);

    /// <summary>Every captured group value of a pattern over one source file, in file order.</summary>
    /// <param name="sources">The sources to scan.</param>
    /// <param name="pattern">The pattern whose first group is captured.</param>
    /// <param name="withLiterals">
    /// Whether the scan needs the string literals (table names) instead of identifier-only text.
    /// </param>
    private static IEnumerable<string> MatchGroup(
        IEnumerable<SourceFile> sources,
        string pattern,
        bool withLiterals = false) =>
        sources.SelectMany(source => MatchGroupFrom(withLiterals ? source.Literals : source.Text, pattern));

    /// <summary>Every captured group value of a pattern over code-only text, in order.</summary>
    private static IEnumerable<string> MatchGroupFrom(string text, string pattern) =>
        Regex.Matches(text, pattern).Select(match => match.Groups[1].Value);

    /// <summary>Whether an identifier names a Tool store/cache/registry/draft.</summary>
    private static bool ContainsToolAndRegistryToken(string identifier) =>
        identifier.Contains("Tool", StringComparison.OrdinalIgnoreCase) &&
        ForbiddenRegistryTokens.Any(token => identifier.Contains(token, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a path lies under <c>bin</c> or <c>obj</c> build output.</summary>
    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    /// <summary>The repository root, located by the directory holding <c>DMO.slnx</c>.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DMO.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
