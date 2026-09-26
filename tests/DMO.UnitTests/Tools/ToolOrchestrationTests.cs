using System.Reflection;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Tools;
using DMO.Web.Endpoints.ToolJobOn;
using DMO.Web.Frontend.Shared.Contracts;
using DMO.Web.Pages.JobOn;

namespace DMO.UnitTests.Tools;

/// <summary>
/// P2-T04 unit — the single shared Tool orchestration and its origin-state boundary: one service
/// contract and one implementation, one opaque→canonical adapter, one explicit selection per
/// interaction, a cancel/return that selects nothing, a pre-fill that invents nothing and an origin
/// token that stays opaque.
/// <para>
/// Authority: <c>plans/contracts/P2-T04_DOMAIN_CORE_TOOL_JOBON_CONTRACT.md</c> §20.7 rows ORC1, ORC2,
/// ORC4, ORC6 and ORC10 and §22 AC-47, AC-48, AC-49, AC-50, AC-52.
/// </para>
/// <para>
/// Preconditions: none; the adapter is constructed from an explicit supplied item list and every
/// assertion is a deterministic in-memory observation.
/// Required non-effects: no per-surface fork of the orchestration, no second resolution of an
/// already-latched selection, no selection from cancel/return, no invented Tool fact in the pre-fill
/// and no canonical identity inside the opaque origin token.
/// </para>
/// </summary>
public sealed class ToolOrchestrationTests
{
    private const string RegionLabel = "Ferramentas do contexto CM";

    /// <summary>The tokens that may only ever name a transport/value carrier (contract §9.1).</summary>
    private static readonly string[] OrchestrationTokens = ["ToolSearch", "ToolSelect", "ToolCreate"];

    /// <summary>
    /// The complete allowed set of types whose name carries one of the orchestration tokens: every one
    /// of them is a value/transport carrier, never a second service, registry or orchestration.
    /// </summary>
    private static readonly string[] AllowedCarrierTypes =
    [
        "DMO.Application.Repositories.ToolSearchCriteria",
        "DMO.Application.Tools.ToolSearchItem",
        "DMO.Application.Tools.ToolSearchQuery",
        "DMO.Application.Tools.ToolSelection",
        "DMO.Web.Endpoints.ToolJobOn.FerramentasEndpoints.ToolCreatedResponse",
        "DMO.Web.Endpoints.ToolJobOn.FerramentasEndpoints.ToolSearchItemResponse",
        "DMO.Web.Endpoints.ToolJobOn.FerramentasEndpoints.ToolSearchResponse",
        "DMO.Web.Pages.JobOn.ToolCreatePrefill",
    ];

    /// <summary>
    /// ORC1 (AC-47) — exactly one Tool search/select/create orchestration exists: <c>IToolService</c>
    /// is the only service contract, <c>ToolService</c> the only implementation, the Job On adapter is
    /// the only opaque→canonical mapping holder, and no other type declares a second
    /// search/select/create orchestration member (the token-carrying types are carriers only).
    /// </summary>
    [Fact]
    public void ORC1_ExactlyOneToolOrchestrationExistsAcrossApplicationAndWeb()
    {
        var application = typeof(IToolService).Assembly;
        var web = typeof(JobOnToolPickerAdapter).Assembly;
        var types = application.GetTypes().Concat(web.GetTypes()).ToList();

        // (a) Exactly one service contract and exactly one implementation of it.
        var resultProducingMethods = types
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.DeclaredOnly)
                .Where(method => method.ReturnType == typeof(Task<ToolResult>))
                .Select(_ => type))
            .Distinct()
            .ToList();

        Assert.Equal([typeof(IToolService), typeof(ToolService)],
            resultProducingMethods.OrderBy(type => type.FullName, StringComparer.Ordinal));

        var implementations = types
            .Where(type => type is { IsClass: true, IsAbstract: false } &&
                           typeof(IToolService).IsAssignableFrom(type))
            .ToList();

        Assert.Equal([typeof(ToolService)], implementations);
        Assert.DoesNotContain(PublicTypes(web), type => typeof(IToolService).IsAssignableFrom(type));

        // (b) The Job On surfaces consume that one contract; none of them forks it.
        foreach (var surface in new[] { typeof(CreateModel), typeof(EditModel) })
        {
            Assert.Contains(
                surface.GetConstructors().SelectMany(constructor => constructor.GetParameters())
                    .Select(parameter => parameter.ParameterType),
                type => type == typeof(IToolService));
            Assert.False(typeof(IToolService).IsAssignableFrom(surface));
        }

        // (c) Exactly one opaque→canonical mapping holder: the adapter's own ResolveCandidate state.
        var resolvers = types
            .Where(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(method => method.Name == "ResolveCandidate"))
            .ToList();

        Assert.Equal([typeof(JobOnToolPickerAdapter)], resolvers);

        // (d) The token-carrying type set is exactly the contracted carrier set ...
        var carriers = PublicTypes(application).Concat(PublicTypes(web))
            .Where(type => OrchestrationTokens.Any(token =>
                type.Name.Contains(token, StringComparison.Ordinal)))
            .ToList();

        Assert.Equal(
            AllowedCarrierTypes.OrderBy(name => name, StringComparer.Ordinal),
            carriers.Select(type => type.FullName!.Replace('+', '.')).OrderBy(name => name, StringComparer.Ordinal));

        // ... and not one of them is an orchestration: no service implementation, no
        // ToolResult-producing member and no member taking an orchestration input.
        foreach (var carrier in carriers)
        {
            Assert.False(typeof(IToolService).IsAssignableFrom(carrier), $"{carrier.Name} is a fork.");
            Assert.DoesNotContain(
                DeclaredSourceMethods(carrier),
                method => method.ReturnType == typeof(Task<ToolResult>) ||
                          method.GetParameters().Any(parameter =>
                              parameter.ParameterType != carrier &&
                              parameter.ParameterType != typeof(CancellationToken) &&
                              (parameter.ParameterType == typeof(ToolSearchQuery) ||
                               parameter.ParameterType == typeof(CreateToolCommand) ||
                               parameter.ParameterType == typeof(ToolSearchCriteria))));
        }

        // (e) No type declares a second search/select/create orchestration method. The only other
        // token-carrying members are the two compile-time page search limits, which are private
        // constants of the consuming surfaces — not a service, a registry or a store.
        var orchestrationMethods = types
            .SelectMany(type => type
                .GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .OfType<MethodBase>()
                .Where(method => OrchestrationTokens.Any(token =>
                    method.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
                .Select(method => $"{type.Name}.{method.Name}"))
            .ToList();

        Assert.Empty(orchestrationMethods);

        var extraTokenMembers = types
            .SelectMany(type => type
                .GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(member => member is not MethodBase and not Type && OrchestrationTokens.Any(token =>
                    member.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
                .Select(member => (Type: type, Member: member)))
            .ToList();

        Assert.NotEmpty(extraTokenMembers);
        Assert.All(extraTokenMembers, entry => Assert.IsType<FieldInfo>(entry.Member, exactMatch: false));
        Assert.All(extraTokenMembers, entry =>
        {
            var field = (FieldInfo)entry.Member;
            Assert.True(field.IsLiteral && !field.IsInitOnly, $"{field.Name} is not a compile-time limit.");
            Assert.False(field.IsPublic, $"{field.Name} is published as a second orchestration surface.");
        });
    }

    /// <summary>
    /// ORC2 (AC-48) — one explicit candidate selection resolves exactly one canonical
    /// <c>tool_id</c> with the slot's expected type, and a second selection cannot change the first
    /// within one interaction; an unknown key resolves nothing and latches nothing.
    /// </summary>
    [Fact]
    public void ORC2_OneSelectionResolvesOneCanonicalToolIdAndIsLatched()
    {
        var items = new[]
        {
            Item("5447T173", "12"),
            Item("5447T173", "13"),
            Item("5448A001", "01"),
        };

        var adapter = new JobOnToolPickerAdapter(ToolContextType.Cm, items);
        var firstKey = adapter.CandidateMap[0].Key;
        var secondKey = adapter.CandidateMap[1].Key;

        Assert.Equal(items[0].ToolId, adapter.ResolveCandidate(firstKey));

        var first = adapter.Select(firstKey);

        Assert.NotNull(first);
        Assert.Equal(items[0].ToolId, first!.ToolId);
        Assert.Equal(ToolType.Cm, first.ExpectedType);
        Assert.Equal(adapter.ExpectedType, first.ExpectedType);

        // A second explicit selection — of another candidate or of the same one — cannot change it.
        var second = adapter.Select(secondKey);

        Assert.Same(first, second);
        Assert.Equal(items[0].ToolId, second!.ToolId);
        Assert.NotEqual(items[1].ToolId, second.ToolId);
        Assert.Equal(items[0].ToolId, adapter.Selection!.ToolId);

        Assert.Same(first, adapter.Select(firstKey));
        Assert.Same(first, adapter.Select("tool-candidate-CM-99"));
        Assert.Equal(items[0].ToolId, adapter.Selection.ToolId);

        // The slot's Tool family is carried by the resolution too: an MF slot resolves an MF Tool.
        var mfItem = new ToolSearchItem(
            Guid.NewGuid(), ToolType.Mf, "5447T173", "12", null, 3, [MachineCode.From("B1")]);
        var mfAdapter = new JobOnToolPickerAdapter(ToolContextType.Mf, [mfItem]);

        var mfSelection = mfAdapter.Select(mfAdapter.CandidateMap[0].Key);

        Assert.NotNull(mfSelection);
        Assert.Equal(mfItem.ToolId, mfSelection!.ToolId);
        Assert.Equal(ToolType.Mf, mfSelection.ExpectedType);

        // An unknown key selects nothing and does not latch: a real explicit selection still works.
        var fresh = new JobOnToolPickerAdapter(ToolContextType.Cm, items);

        Assert.Null(fresh.Select("tool-candidate-CM-99"));
        Assert.Null(fresh.Select("Linha B"));
        Assert.Null(fresh.Select(string.Empty));
        Assert.Null(fresh.Selection);
        Assert.Null(fresh.AssociatedToolId);

        var valid = fresh.Select(fresh.CandidateMap[2].Key);

        Assert.NotNull(valid);
        Assert.Equal(items[2].ToolId, valid!.ToolId);
        Assert.Equal(items[2].ToolId, fresh.Selection!.ToolId);
    }

    /// <summary>
    /// ORC4 (AC-50) — <c>Cancel()</c> and <c>Return()</c> select nothing and leave the origin
    /// association exactly as it was, whether it was empty, held a selected Tool or held a Tool
    /// created inline in the origin subflow.
    /// </summary>
    [Fact]
    public void ORC4_CancelAndReturnSelectNothingAndPreserveTheOriginAssociation()
    {
        var items = new[]
        {
            Item("5447T173", "12"),
            Item("5447T173", "13"),
        };

        // No association at all: cancelling still associates nothing.
        var empty = new JobOnToolPickerAdapter(ToolContextType.Cm, items);

        Assert.Null(empty.AssociatedToolId);
        Assert.Null(empty.Cancel());
        Assert.Null(empty.Selection);
        Assert.Null(empty.AssociatedToolId);

        Assert.Null(empty.Return());
        Assert.Null(empty.Selection);
        Assert.Null(empty.AssociatedToolId);

        // An existing association survives both a cancel and a return, and neither selects anything.
        var adapter = new JobOnToolPickerAdapter(ToolContextType.Cm, items);
        adapter.Associate(items[0].ToolId);

        Assert.Equal(items[0].ToolId, adapter.AssociatedToolId);

        Assert.NotNull(adapter.Select(adapter.CandidateMap[1].Key));
        Assert.Equal(items[1].ToolId, adapter.Selection!.ToolId);

        Assert.Null(adapter.Cancel());
        Assert.Null(adapter.Selection);
        Assert.Equal(items[0].ToolId, adapter.AssociatedToolId);

        Assert.NotNull(adapter.Select(adapter.CandidateMap[0].Key));

        Assert.Null(adapter.Return());
        Assert.Null(adapter.Selection);
        Assert.Equal(items[0].ToolId, adapter.AssociatedToolId);

        // An inline-created canonical identity is an association too, not a selection.
        var inline = new JobOnToolPickerAdapter(ToolContextType.Cm, []);
        var createdToolId = Guid.NewGuid();
        inline.Associate(createdToolId);

        Assert.Null(inline.Cancel());
        Assert.Null(inline.Return());
        Assert.Null(inline.Selection);
        Assert.Equal(createdToolId, inline.AssociatedToolId);

        // Only an explicit clearing removes it.
        inline.ClearAssociation();
        Assert.Null(inline.AssociatedToolId);
    }

    /// <summary>
    /// ORC6 (AC-52) — the inline create pre-fill carries only values already known from the origin
    /// context: the reference alone. Lot, <c>processo</c>, quantity and machines are never invented
    /// or defaulted, even when the searched candidates do carry those facts.
    /// </summary>
    [Fact]
    public void ORC6_PrefillCarriesOnlyTheKnownReferenceAndInventsNoToolFact()
    {
        var known = new ToolSearchItem(
            Guid.NewGuid(),
            ToolType.Cm,
            "5447T173",
            "12",
            Processo.Nnpb,
            7,
            [MachineCode.From("B1"), MachineCode.From("C2")]);

        var adapter = new JobOnToolPickerAdapter(ToolContextType.Cm, [known]);

        // The candidate really does carry the other facts, so the pre-fill is not empty by accident.
        Assert.Contains(adapter.Candidates[0].Facts, fact => fact.Label == "Lote" && fact.Value == "12");
        Assert.Contains(adapter.Candidates[0].Facts, fact => fact.Label == "Quantidade" && fact.Value == "7");

        var prefill = adapter.Prefill("5447T173");

        Assert.Equal("5447T173", prefill.Reference);
        Assert.Null(prefill.Lot);
        Assert.Null(prefill.Processo);
        Assert.Null(prefill.Quantity);
        Assert.Empty(prefill.Machines);

        // Nothing known from the origin → nothing supplied at all.
        var unknown = adapter.Prefill(null);

        Assert.Null(unknown.Reference);
        Assert.Null(unknown.Lot);
        Assert.Null(unknown.Processo);
        Assert.Null(unknown.Quantity);
        Assert.Empty(unknown.Machines);

        // The pre-fill is independent of the searched result: a second adapter over other items with
        // the same known reference carries exactly the same single fact and nothing more.
        var other = new JobOnToolPickerAdapter(
            ToolContextType.Cm,
            [new ToolSearchItem(Guid.NewGuid(), ToolType.Cm, "5447T173", "99", Processo.Ps, 0, [MachineCode.From("C3")])])
            .Prefill("5447T173");

        Assert.Equal(prefill.Reference, other.Reference);
        Assert.Null(other.Lot);
        Assert.Null(other.Processo);
        Assert.Null(other.Quantity);
        Assert.Empty(other.Machines);
    }

    /// <summary>
    /// ORC10 (AC-49) — the adapter's origin token round-trips verbatim into the built picker
    /// presentation, is derived from the slot only (never from a canonical identity) and never takes a
    /// URL, route, filename or path shape.
    /// </summary>
    [Fact]
    public void ORC10_OriginTokenRoundTripsVerbatimAndIsNeverAUrl()
    {
        var items = new[]
        {
            Item("5447T173", "12"),
            Item("5447T173", "13"),
            Item("5448A001", "01"),
        };

        var adapter = new JobOnToolPickerAdapter(ToolContextType.Cm, items);
        var presentation = adapter.BuildPresentation(CommonState.Ready, RegionLabel);

        var token = adapter.OriginToken;

        // Verbatim round trip — no re-derivation on the way into the presentation.
        Assert.NotNull(presentation.OriginToken);
        Assert.Equal(token, presentation.OriginToken);
        Assert.Equal("jobon-tool-origin-CM", token);

        // Derived from the slot only: independent of the canonical result set and of its size.
        var sameSlotOtherItems = new JobOnToolPickerAdapter(
            ToolContextType.Cm,
            [Item("9999Z999", "77")]).OriginToken;
        var emptySlot = new JobOnToolPickerAdapter(ToolContextType.Cm, []).OriginToken;
        var otherSlot = new JobOnToolPickerAdapter(ToolContextType.Mf, items).OriginToken;

        Assert.Equal(token, sameSlotOtherItems);
        Assert.Equal(token, emptySlot);
        Assert.NotEqual(token, otherSlot);
        Assert.Equal("jobon-tool-origin-MF", otherSlot);

        // No URL/route/filename/path shape anywhere in the opaque values of this interaction.
        foreach (var opaque in new[] { token, presentation.OriginToken! }
                     .Concat(adapter.CandidateMap.Select(entry => entry.Key))
                     .Concat(presentation.Candidates.Select(candidate => candidate.Key)))
        {
            Assert.DoesNotContain("/", opaque);
            Assert.DoesNotContain("\\", opaque);
            Assert.DoesNotContain("?", opaque);
            Assert.DoesNotContain("&", opaque);
            Assert.DoesNotContain(":", opaque);
            Assert.DoesNotContain("#", opaque);
            Assert.False(opaque.StartsWith('/'), $"'{opaque}' is a path, not an opaque token.");
            Assert.DoesNotContain(FerramentasEndpoints.FerramentasBasePath, opaque, StringComparison.Ordinal);
        }

        // No canonical identity is embedded in the token or in a candidate key.
        foreach (var item in items)
        {
            Assert.DoesNotContain(item.ToolId.ToString("D"), token, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(item.ToolId.ToString("N"), token, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(item.Reference, token, StringComparison.Ordinal);
            Assert.DoesNotContain(item.Lot, token, StringComparison.Ordinal);

            foreach (var key in adapter.CandidateMap
                         .Where(entry => entry.ToolId == item.ToolId)
                         .Select(entry => entry.Key))
            {
                Assert.DoesNotContain(item.ToolId.ToString("D"), key, StringComparison.OrdinalIgnoreCase);
            }
        }

        // The token is an opaque label, not an address the adapter can resolve: only the adapter's own
        // candidate keys resolve, and the token itself resolves to nothing.
        Assert.Null(adapter.ResolveCandidate(token));
    }

    /// <summary>One canonical Tool search item with a fresh identity.</summary>
    private static ToolSearchItem Item(string reference, string lot) =>
        new(
            Guid.NewGuid(),
            ToolType.Cm,
            reference,
            lot,
            null,
            null,
            [MachineCode.From("B1")]);

    /// <summary>Every public type of an assembly, nested public types included.</summary>
    private static IEnumerable<Type> PublicTypes(Assembly assembly) =>
        assembly.GetTypes().Where(type => type.IsPublic || type.IsNestedPublic);

    /// <summary>
    /// The methods a type really owns as source: no property accessors, no operators and none of the
    /// synthesized record members, so a carrier's own generated surface is never read as orchestration.
    /// </summary>
    private static IReadOnlyList<MethodInfo> DeclaredSourceMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Where(method => !method.IsDefined(
                typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false))
            .ToList();
}
