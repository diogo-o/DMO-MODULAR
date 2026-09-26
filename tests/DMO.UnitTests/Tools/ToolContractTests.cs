using System.Reflection;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Tools;
using DMO.Web.Endpoints.ToolJobOn;
using DMO.Web.Frontend.Shared.Contracts;
using DMO.Web.Pages.JobOn;

namespace DMO.UnitTests.Tools;

/// <summary>
/// P2-T04 unit — the contracted Tool carriers: the nullable quantity fact, the exact search-query
/// member set with no ranking member, the settled machine-code set, and the candidate mapping that
/// never auto-selects.
/// <para>
/// Authority: <c>plans/contracts/P2-T04_DOMAIN_CORE_TOOL_JOBON_CONTRACT.md</c> §20.1 rows TOL3, TOL8,
/// TOL10, TOL23 and TOL24 and §22 AC-6, AC-7, AC-9, AC-10, AC-17, AC-22.
/// </para>
/// <para>
/// Preconditions: none; every carrier is a deterministic in-memory value type and the adapter is
/// constructed from an explicit supplied item list.
/// Required non-effects: no absence is rendered as <c>0</c>, no member invents a ranking or
/// best-match rule, no machine grouping exists, and no mapping step selects a candidate.
/// </para>
/// </summary>
public sealed class ToolContractTests
{
    private const string RegionLabel = "Ferramentas do contexto CM";

    /// <summary>The exact contracted member set of <see cref="ToolSearchQuery"/> (contract §8.2).</summary>
    private static readonly string[] ContractedQueryMembers =
        ["Query", "Type", "Reference", "Lot", "Machine", "Limit"];

    /// <summary>The invented-ranking tokens no Tool search member may carry (AC-10).</summary>
    private static readonly string[] RankingTokens =
        ["rank", "score", "relevance", "best", "newest", "latest"];

    /// <summary>The invented machine-grouping tokens no machine member may carry (AC-6, AC-17).</summary>
    private static readonly string[] GroupingTokens =
        ["line", "linha", "group", "grupo", "parent", "child", "pai", "filho", "cascade", "hierarch"];

    /// <summary>
    /// TOL3 (AC-7) — <c>Quantity = null</c> stays <c>null</c> through the Tool read models (and is
    /// never replaced by <c>0</c>), a Tool with quantity <c>0</c> keeps the real value <c>0</c>, and
    /// the Job On candidate facts omit the quantity fact entirely when it is unknown while rendering
    /// <c>"0"</c> when the Tool really holds <c>0</c>.
    /// </summary>
    [Fact]
    public void TOL3_NullQuantityStaysNullAndZeroQuantityStaysZero()
    {
        var unknownQuantityItem = Item("5447T173", "12", quantity: null);
        var zeroQuantityItem = Item("5447T173", "12", quantity: 0);

        Assert.Null(unknownQuantityItem.Quantity);
        Assert.Null(new Tool(
            ToolId.New(), ToolType.Cm, "5447T173", "12", null, null, [MachineCode.From("B1")]).Quantity);
        Assert.Null(new ToolFicha(
            Guid.NewGuid(), ToolType.Cm, "5447T173", "12", null, null, [MachineCode.From("B1")], []).Quantity);

        var zeroTool = new Tool(
            ToolId.New(), ToolType.Cm, "5447T173", "12", Processo.Nnpb, 0, [MachineCode.From("B1")]);

        Assert.NotNull(zeroTool.Quantity);
        Assert.Equal(0, zeroTool.Quantity!.Value);
        Assert.Equal(0, zeroQuantityItem.Quantity!.Value);
        Assert.Equal(0, new ToolFicha(
            Guid.NewGuid(), ToolType.Cm, "5447T173", "12", null, 0, [MachineCode.From("B1")], []).Quantity!.Value);

        // Unknown and zero are two different states, never collapsed into one.
        Assert.False(unknownQuantityItem.Quantity.HasValue);
        Assert.True(zeroTool.Quantity.HasValue);

        // The origin surface: an unknown quantity supplies NO fact at all (never a dash, never "0").
        var unknownAdapter = new JobOnToolPickerAdapter(ToolContextType.Cm, [unknownQuantityItem]);
        var unknownFacts = unknownAdapter.Candidates[0].Facts;

        Assert.DoesNotContain(unknownFacts, fact => fact.Label == "Quantidade");
        Assert.DoesNotContain(unknownFacts, fact => fact.Value == "0");
        Assert.Contains(unknownFacts, fact => fact.Label == "Lote" && fact.Value == "12");

        // A real zero is a real fact and renders as "0".
        var zeroAdapter = new JobOnToolPickerAdapter(ToolContextType.Cm, [zeroQuantityItem]);
        var quantityFact = Assert.Single(
            zeroAdapter.Candidates[0].Facts,
            fact => fact.Label == "Quantidade");

        Assert.Equal("0", quantityFact.Value);
    }

    /// <summary>
    /// TOL8 (AC-10) — <see cref="ToolSearchQuery"/> declares exactly the six contracted members and
    /// the Tool search surface exposes no rank/score/relevance/"best match"/latest/newest member
    /// anywhere.
    /// </summary>
    [Fact]
    public void TOL8_SearchQueryHasExactlyTheContractedMembersAndNoRankingMember()
    {
        var properties = typeof(ToolSearchQuery)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Equal(ContractedQueryMembers, properties.Select(property => property.Name));

        Assert.Equal(typeof(string), Property("Query").PropertyType);
        Assert.Equal(typeof(ToolType?), Property("Type").PropertyType);
        Assert.Equal(typeof(string), Property("Reference").PropertyType);
        Assert.Equal(typeof(string), Property("Lot").PropertyType);
        Assert.Equal(typeof(MachineCode?), Property("Machine").PropertyType);
        Assert.Equal(typeof(int), Property("Limit").PropertyType);

        // No extra carrier member hides outside the positional property set either: every instance
        // field is the generated backing field of one of the six contracted members.
        Assert.All(
            typeof(ToolSearchQuery).GetFields(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly),
            field => Assert.Contains("k__BackingField", field.Name, StringComparison.Ordinal));

        // No ranking, scoring, relevance or "best match" member exists on any Tool search carrier:
        // not on the query, the repository criteria, the result item, the service, the validator, the
        // endpoint surface or the Job On adapter.
        var searchTypes = ToolSearchSurfaceTypes().ToList();
        var memberNames = searchTypes
            .SelectMany(type => type
                .GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(member => $"{type.Name}.{member.Name}"))
            .ToList();

        Assert.NotEmpty(memberNames);
        Assert.Contains(memberNames, name => name.EndsWith(".SearchAsync", StringComparison.Ordinal));
        Assert.All(
            memberNames,
            name => Assert.All(
                RankingTokens,
                token => Assert.DoesNotContain(token, name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// TOL10 (AC-6, AC-17) — <c>MachineCode.All</c> is exactly <c>B1,B2,B3,C1,C2,C3</c> in the
    /// settled order, the type exposes no line/group/linha/parent member or relation, and
    /// <c>"Linha B"</c>, <c>"B4"</c> and <c>"B"</c> are rejected by both <c>IsKnown</c> and
    /// <c>Parse</c>.
    /// </summary>
    [Fact]
    public void TOL10_MachineCodesAreExactlyTheSixSettledCodesWithoutGrouping()
    {
        Assert.Equal(["B1", "B2", "B3", "C1", "C2", "C3"], MachineCode.All.Select(machine => machine.Value));
        Assert.Equal(6, MachineCode.All.Count);
        Assert.Equal(6, MachineCode.All.Select(machine => machine.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal([0, 1, 2, 3, 4, 5], MachineCode.All.Select(machine => machine.Order));

        // The type is a flat code value: no line/group/linha member, no parent/child relation.
        var memberNames = typeof(MachineCode)
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .ToList();

        Assert.NotEmpty(memberNames);
        Assert.Contains("IsKnown", memberNames);
        Assert.All(
            memberNames,
            name => Assert.All(
                GroupingTokens,
                token => Assert.DoesNotContain(token, name, StringComparison.OrdinalIgnoreCase)));

        // The type holds no collection member that could represent a parent/child or shared lineage.
        Assert.All(
            typeof(MachineCode)
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                               BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(property => property.GetMethod?.IsStatic == false),
            property => Assert.False(
                property.PropertyType != typeof(string) &&
                typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType),
                $"'{property.Name}' exposes a collection member on the flat machine code."));

        // Only the six settled codes are known and parseable; a fabricated grouping value is not.
        foreach (var settled in new[] { "B1", "B2", "B3", "C1", "C2", "C3" })
        {
            Assert.True(MachineCode.IsKnown(settled));
            Assert.NotNull(MachineCode.Parse(settled));
        }

        foreach (var rejected in new[] { "Linha B", "LINHA B", "Linha C", "B4", "B", "C", "b1", "B1 " })
        {
            Assert.False(MachineCode.IsKnown(rejected), $"'{rejected}' must not be a settled machine.");
            Assert.Null(MachineCode.Parse(rejected));
        }

        // An unsettled wrapped value has no settled ordering position, so it can never pass as a
        // member of the six settled machines.
        Assert.Equal(-1, MachineCode.From("Linha B").Order);
        Assert.Equal(-1, MachineCode.From("B4").Order);
    }

    /// <summary>
    /// TOL23 (AC-9) — mapping N search items produces N candidates in the supplied order, with
    /// distinct non-blank keys and <c>SelectedCandidateKey = null</c>: ambiguity stays explicit.
    /// </summary>
    [Fact]
    public void TOL23_MappingThreeSearchItemsKeepsSuppliedOrderAndSelectsNothing()
    {
        var items = new[]
        {
            Item("5447T173", "13", quantity: 4),
            Item("5447T173", "11", quantity: null),
            Item("5447T173", "12", quantity: 0),
        };

        var adapter = new JobOnToolPickerAdapter(ToolContextType.Cm, items);
        var presentation = adapter.BuildPresentation(CommonState.Ready, RegionLabel);

        Assert.Equal(3, adapter.Candidates.Count);
        Assert.Equal(3, presentation.Candidates.Count);

        // Supplied order, proved by the canonical identity behind each candidate and by its content.
        Assert.Equal(items.Select(item => item.ToolId), adapter.CandidateMap.Select(entry => entry.ToolId));
        Assert.Equal(items.Select(item => item.Lot), presentation.Candidates.Select(CandidateLot));

        var keys = presentation.Candidates.Select(candidate => candidate.Key).ToList();

        Assert.Equal(adapter.Candidates.Select(candidate => candidate.Key), keys);
        Assert.Equal(3, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, key => Assert.False(string.IsNullOrWhiteSpace(key)));

        // Never auto-selected — not even the first one.
        Assert.Null(presentation.SelectedCandidateKey);
        Assert.All(keys, key => Assert.False(presentation.IsCandidateSelected(key)));
        Assert.Null(adapter.Selection);
    }

    /// <summary>
    /// TOL24 (AC-9, AC-22) — mapping exactly one search item produces one candidate that keeps its
    /// own select control and <c>SelectedCandidateKey = null</c>: a single result never
    /// auto-resolves.
    /// </summary>
    [Fact]
    public void TOL24_MappingOneSearchItemProducesOneCandidateWithNoAutoSelection()
    {
        var only = Item("5447T173", "12", quantity: 7);

        var adapter = new JobOnToolPickerAdapter(ToolContextType.Cm, [only]);
        var presentation = adapter.BuildPresentation(CommonState.Ready, RegionLabel);

        var candidate = Assert.Single(presentation.Candidates);

        Assert.Single(adapter.Candidates);
        Assert.Equal(only.ToolId, Assert.Single(adapter.CandidateMap).ToolId);
        Assert.Equal("12", CandidateLot(candidate));

        // The candidate owns its own select control and is enabled, because no subflow is pending.
        Assert.False(string.IsNullOrWhiteSpace(candidate.Key));
        Assert.True(presentation.SelectControlsEnabled);

        // ... and it is still not selected.
        Assert.Null(presentation.SelectedCandidateKey);
        Assert.False(presentation.IsCandidateSelected(candidate.Key));
        Assert.Null(adapter.Selection);
        Assert.Equal("Selecionar", ToolPickerPresentation.GenericSelectControlLabel);
    }

    /// <summary>One canonical Tool search item with a fresh identity.</summary>
    private static ToolSearchItem Item(string reference, string lot, int? quantity) =>
        new(
            Guid.NewGuid(),
            ToolType.Cm,
            reference,
            lot,
            null,
            quantity,
            [MachineCode.From("B1"), MachineCode.From("C2")]);

    /// <summary>The supplied lot of a candidate, read back from its own supplied facts.</summary>
    private static string CandidateLot(ToolPickerCandidatePresentation candidate) =>
        candidate.Facts.Single(fact => fact.Label == "Lote").Value;

    /// <summary>A contracted query property by name.</summary>
    private static PropertyInfo Property(string name) =>
        typeof(ToolSearchQuery).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"'{name}' is not a declared Tool search query member.");

    /// <summary>Every type forming the Tool search surface whose members must not rank anything.</summary>
    private static IEnumerable<Type> ToolSearchSurfaceTypes()
    {
        var application = typeof(ToolSearchQuery).Assembly;
        var web = typeof(JobOnToolPickerAdapter).Assembly;

        var types = new List<Type>
        {
            typeof(ToolSearchQuery),
            typeof(ToolSearchCriteria),
            typeof(ToolSearchItem),
            typeof(ToolFicha),
            typeof(ToolSelection),
            typeof(ToolResult),
            typeof(ToolTokens),
            typeof(ToolValidator),
            typeof(ToolValidationErrors),
            typeof(CreateToolCommand),
            typeof(IToolService),
            typeof(ToolService),
            typeof(IToolRepository),
            typeof(ToolUsageOccurrence),
            typeof(MachineCode),
            typeof(ToolCompatibility),
            typeof(JobOnToolPickerAdapter),
            typeof(JobOnSlotTokens),
            typeof(ToolCandidateEntry),
            typeof(ToolCreatePrefill),
            typeof(FerramentasEndpoints),
        };

        types.AddRange(application.GetTypes().Where(type => type.Namespace == "DMO.Application.Tools"));
        types.AddRange(web.GetTypes().Where(type =>
            type.Namespace is "DMO.Web.Endpoints.ToolJobOn" or "DMO.Web.Pages.JobOn" &&
            type.Name.Contains("Tool", StringComparison.Ordinal)));

        return types.Distinct();
    }
}
