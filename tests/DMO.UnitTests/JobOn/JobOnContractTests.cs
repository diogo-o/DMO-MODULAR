using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DMO.Application.JobOn;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.JobOn;
using DMO.Domain.Tools;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Entities;
using DMO.Web.Endpoints;
using DMO.Web.Frontend.Shared.Contracts;
using DMO.Web.Pages.JobOn;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using DomainJobOn = DMO.Domain.JobOn.JobOn;

namespace DMO.UnitTests.JobOn;

/// <summary>
/// P2-T04 unit and static proofs of the Job On occurrence contract: rows JOB3, JOB4, JOB18, JOB19,
/// JOB20, JOB21, JOB22, CTX9, CTX11, CTX16, DUP13, DUP16, ORC3, ORC7, ORC8, DEP1, DEP11 and DEP12 of
/// the test-to-acceptance matrix
/// (<c>plans/contracts/P2-T04_DOMAIN_CORE_TOOL_JOBON_CONTRACT.md</c> §20.2–§20.7).
/// </summary>
/// <remarks>
/// Every row here is a boundary proof: what the contracted carriers expose and, more importantly,
/// what no P2-T04 type, member, route or schema column is allowed to introduce. Reflection reads the
/// real compiled types; the source scans read the real repository files found by walking up to
/// <c>DMO.slnx</c>.
/// </remarks>
public sealed class JobOnContractTests
{
    /// <summary>The six P2-T04 persistence entities (contract §3.1–§3.4, Appendix B.3).</summary>
    private static readonly Type[] P2T04Entities =
    [
        typeof(ToolEntity),
        typeof(ToolMachineEntity),
        typeof(JobOnEntity),
        typeof(CmContextEntity),
        typeof(MfContextEntity),
        typeof(BqContextEntity),
    ];

    /// <summary>The Job On/tool tables the migration owns, as names (contract §3, AC-103).</summary>
    private static readonly string[] ContractedTableNames =
    [
        "bq_contexts",
        "cm_contexts",
        "job_ons",
        "mf_contexts",
        "tool_machines",
        "tools",
    ];

    /// <summary>The closed status/lifecycle vocabulary that must appear nowhere (AC-15).</summary>
    private static readonly string[] LifecycleVocabulary =
    [
        "status", "rascunho", "planeado", "fabrico", "fechado", "cancelado",
        "active", "locked", "approved", "state", "lifecycle", "revision",
    ];

    /// <summary>Non-nullable placeholder connection string; never contacted (model building only).</summary>
    private const string PlaceholderConnectionString =
        "Host=localhost;Port=5432;Database=dmo_placeholder;Username=placeholder;Password=placeholder";

    // ---------------------------------------------------------------------------------------------
    // 20.2 Job On occurrence, create, reference query, edit
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// JOB3 — no Job On type, command, result, route carrier or route template names or represents a
    /// lifecycle status: the status vocabulary scan finds no member, no type and no route token.
    /// Proves AC-15.
    /// </summary>
    [Fact]
    public void JOB3_NoJobOnTypeCommandResultOrRouteDeclaresALifecycleStatus()
    {
        var types = JobOnContractTypes();
        Assert.NotEmpty(types);

        var names = types
            .Select(type => type.Name)
            .Concat(types.SelectMany(ContractMemberNames))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(names);
        Assert.Empty(Offenders(names, LifecycleVocabulary));

        // The declared members of the route-table carriers are part of the same scan.
        Assert.Contains("JobOnId", names);

        // The declared route table: the Job On endpoint templates plus the Job On page routes.
        var templates = JobOnRouteTemplates();
        Assert.NotEmpty(templates);
        Assert.Contains("/jobon", templates);
        Assert.Empty(Offenders(templates, LifecycleVocabulary));
    }

    /// <summary>
    /// JOB4 — the Job On domain record and its persistence entity have exactly the contracted members
    /// (the four captured facts, the identity, the lineage, the version and the contexts) and no
    /// <c>processo</c>, <c>productionId</c>, <c>revisionId</c>, quantity or status member.
    /// Proves AC-13, AC-14 and AC-98.
    /// </summary>
    [Fact]
    public void JOB4_TheJobOnRecordAndEntityHaveExactlyTheContractedMembers()
    {
        var jobOn = PropertyMap(typeof(DomainJobOn));
        Assert.Equal(
            new[]
            {
                "Contexts", "CopiedFromJobOnId", "JobOnId", "Machine",
                "ProductionDate", "ProductionNumber", "Reference", "Version",
            },
            jobOn.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(typeof(JobOnId), jobOn["JobOnId"]);
        Assert.Equal(typeof(string), jobOn["Reference"]);
        Assert.Equal(typeof(string), jobOn["ProductionNumber"]);
        Assert.Equal(typeof(MachineCode), jobOn["Machine"]);
        Assert.Equal(typeof(DateOnly?), jobOn["ProductionDate"]);
        Assert.Equal(typeof(Guid?), jobOn["CopiedFromJobOnId"]);
        Assert.Equal(typeof(int), jobOn["Version"]);
        Assert.Equal(typeof(IReadOnlyList<ToolContext>), jobOn["Contexts"]);

        // The persistence entity mirrors the same facts plus the two system timestamps: no seventh
        // fact and no Tool master fact is copied into the occurrence.
        var entity = PropertyMap(typeof(JobOnEntity));
        Assert.Equal(
            new[]
            {
                "CopiedFromJobOnId", "CreatedAt", "JobOnId", "Machine", "ProductionDate",
                "ProductionNumber", "Reference", "UpdatedAt", "Version",
            },
            entity.Keys.OrderBy(name => name, StringComparer.Ordinal));

        string[] absent = ["processo", "productionid", "revisionid", "quantity", "status", "state", "lifecycle"];
        Assert.Empty(Offenders(jobOn.Keys, absent));
        Assert.Empty(Offenders(entity.Keys, absent));
        Assert.Empty(Offenders(ContractMemberNames(typeof(DomainJobOn)), absent));
        Assert.Empty(Offenders(ContractMemberNames(typeof(JobOnEntity)), absent));
    }

    /// <summary>
    /// JOB18 — the productions item carrier exposes exactly the occurrence identity plus the human
    /// production facts: no context id, no Tool fact and no document/Controlo field. Proves AC-40 and
    /// AC-46.
    /// </summary>
    [Fact]
    public void JOB18_TheProductionsItemCarrierExposesIdentityAndHumanFactsOnly()
    {
        var item = PropertyMap(typeof(JobOnProductionListItem));
        Assert.Equal(
            new[] { "JobOnId", "Machine", "ProductionDate", "ProductionNumber", "Reference" },
            item.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(typeof(Guid), item["JobOnId"]);
        Assert.Equal(typeof(string), item["Reference"]);
        Assert.Equal(typeof(string), item["ProductionNumber"]);
        Assert.Equal(typeof(string), item["Machine"]);
        Assert.Equal(typeof(DateOnly?), item["ProductionDate"]);

        string[] foreignFacts =
        [
            "context", "tool", "processo", "quantity", "document", "controlo", "boquilha",
            "status", "availability", "condition", "stock", "folder", "file", "pdf", "note",
        ];
        Assert.Empty(Offenders(item.Keys, foreignFacts));

        // The transport carrier of the same read is the same five facts and nothing else.
        var response = PropertyMap(typeof(JobOnEndpoints.JobOnProductionItemResponse));
        Assert.Equal(
            new[] { "JobonId", "Machine", "ProductionDate", "ProductionNumber", "Reference" },
            response.Keys.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// JOB19 — the Tool search item carrier exposes exactly the canonical identity plus Tool-owned
    /// facts: no status, availability, condition, stock or verdict member. Proves AC-41.
    /// </summary>
    [Fact]
    public void JOB19_TheToolSearchItemCarrierExposesIdentityAndToolOwnedFactsOnly()
    {
        var item = PropertyMap(typeof(ToolSearchItem));
        Assert.Equal(
            new[]
            {
                "CompatibleMachines", "Lot", "Processo", "Quantity", "Reference", "ToolId", "Type",
            },
            item.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(typeof(Guid), item["ToolId"]);
        Assert.Equal(typeof(ToolType), item["Type"]);
        Assert.Equal(typeof(string), item["Reference"]);
        Assert.Equal(typeof(string), item["Lot"]);
        Assert.Equal(typeof(Processo?), item["Processo"]);
        Assert.Equal(typeof(int?), item["Quantity"]);
        Assert.Equal(typeof(IReadOnlyList<MachineCode>), item["CompatibleMachines"]);

        string[] inventedFacts =
        [
            "status", "availability", "condition", "stock", "verdict", "compatiblewith",
            "latest", "newest", "best", "rank", "score", "relevance", "location", "utilisation",
        ];
        Assert.Empty(Offenders(item.Keys, inventedFacts));

        // The transport carrier of the same read is the same seven facts and nothing else.
        var response = PropertyMap(typeof(FerramentasEndpoints.ToolSearchItemResponse));
        Assert.Equal(
            new[]
            {
                "CompatibleMachines", "Lot", "Processo", "Quantity", "Reference", "ToolId", "Type",
            },
            response.Keys.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// JOB20 — no contracted query is unbounded: the reference → productions query carries exactly its
    /// required reference, and the Tool search requires at least one criterion inside the bounded
    /// limit. Proves AC-39 and AC-42.
    /// </summary>
    [Fact]
    public void JOB20_NoContractedQueryExposesAnUnboundedList()
    {
        // The reference → productions query: one required member, no paging and no "all" switch.
        var productions = PropertyMap(typeof(FindProductionsQuery));
        Assert.Equal(new[] { "Reference" }, productions.Keys);
        Assert.Equal(typeof(string), productions["Reference"]);

        var reference = Assert.Single(typeof(FindProductionsQuery).GetConstructors().Single().GetParameters());
        Assert.Equal("Reference", reference.Name);
        Assert.False(reference.HasDefaultValue);
        Assert.Equal(NullabilityState.NotNull, new NullabilityInfoContext().Create(reference).ReadState);

        string[] wideningTokens = ["limit", "offset", "page", "skip", "take", "top", "size", "all", "unbounded", "map"];
        Assert.Empty(Offenders(productions.Keys, wideningTokens));

        // A blank reference is refused before the read: never a whole-registry answer.
        Assert.Equal(
            new[] { JobOnValidationErrors.ReferenceRequired },
            JobOnValidator.Validate(new FindProductionsQuery("   ")));

        // The Tool search: a bounded limit member owned by the validator, plus at least one criterion.
        var search = PropertyMap(typeof(ToolSearchQuery));
        Assert.Equal(typeof(int), search["Limit"]);
        Assert.Empty(Offenders(search.Keys, wideningTokens.Where(token => token != "limit")));
        Assert.Equal(100, ToolValidator.MaxLimit);
        Assert.Equal(1, ToolValidator.MinLimit);

        Assert.Equal(
            new[] { ToolValidationErrors.LimitOutOfRange },
            ToolValidator.Validate(new ToolSearchQuery(null, ToolType.Cm, null, null, null, ToolValidator.MaxLimit + 1)));
        Assert.Equal(
            new[] { ToolValidationErrors.LimitOutOfRange },
            ToolValidator.Validate(new ToolSearchQuery(null, ToolType.Cm, null, null, null, ToolValidator.MinLimit - 1)));
        Assert.Empty(ToolValidator.Validate(new ToolSearchQuery(null, ToolType.Cm, null, null, null, ToolValidator.MaxLimit)));
        Assert.Empty(ToolValidator.Validate(new ToolSearchQuery(null, ToolType.Cm, null, null, null, ToolValidator.MinLimit)));

        Assert.Equal(
            new[] { ToolValidationErrors.SearchCriteriaRequired },
            ToolValidator.Validate(new ToolSearchQuery(null, null, null, null, null, ToolValidator.MaxLimit)));
        Assert.Equal(
            new[] { ToolValidationErrors.SearchCriteriaRequired },
            ToolValidator.Validate(new ToolSearchQuery("   ", null, "  ", " ", null, ToolValidator.MaxLimit)));

        // One criterion is enough, so no consumer ever has to widen the query to get an answer.
        Assert.Empty(ToolValidator.Validate(new ToolSearchQuery(null, null, null, "12", null, ToolValidator.MaxLimit)));
    }

    /// <summary>
    /// JOB21 — no P2-T04 code path derives a canonical identity from display text: no Guid is parsed
    /// out of a reference/lot/machine value, the identity factories only ever receive an id carrier,
    /// and no application contract member turns display text into an identity. Proves AC-43.
    /// </summary>
    [Fact]
    public void JOB21_NoP2T04CodePathDerivesACanonicalIdentityFromDisplayText()
    {
        var sources = ApplicationAndJobOnWebSources();
        Assert.NotEmpty(sources);

        // (1) A Guid is never parsed out of a value: the only parse in the scanned tree takes a
        // literal, fixed constant.
        var guidLiteral = new Regex(
            "^\\s*\"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\"\\s*$",
            RegexOptions.Compiled);

        var parses = sources
            .SelectMany(file => Regex.Matches(file.Text, @"Guid\.Parse\((?<argument>[^)]*)\)")
                .Select(match => (file.RelativePath, Argument: match.Groups["argument"].Value)))
            .ToList();
        foreach (var (path, argument) in parses)
        {
            Assert.True(
                guidLiteral.IsMatch(argument),
                $"{path} parses a Guid out of the value '{argument}', which is not a fixed literal.");
        }

        foreach (var file in sources)
        {
            Assert.DoesNotContain("new Guid(", file.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Guid.TryParse(", file.Text, StringComparison.Ordinal);
        }

        // (2) The identity factories receive an id carrier and nothing else: never a string, never a
        // display-text member, never a parsed or formatted value.
        var simpleCarrier = new Regex("^[A-Za-z_][A-Za-z0-9_]*(\\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.Compiled);
        var factories = sources
            .SelectMany(file => Regex.Matches(file.Text, @"(?<factory>ToolId|JobOnId)\.From\((?<argument>[^)]*)\)")
                .Select(match => (
                    file.RelativePath,
                    file.Text,
                    Factory: match.Groups["factory"].Value,
                    Argument: match.Groups["argument"].Value.Trim())))
            .ToList();
        Assert.NotEmpty(factories);

        foreach (var factory in factories)
        {
            Assert.True(
                simpleCarrier.IsMatch(factory.Argument),
                $"{factory.RelativePath} feeds {factory.Factory}.From with the expression '{factory.Argument}'.");
            Assert.DoesNotContain("reference", factory.Argument, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("lot", factory.Argument, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("machine", factory.Argument, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("productionnumber", factory.Argument, StringComparison.OrdinalIgnoreCase);

            // The carrier is never string-typed in its own file, so it cannot be display text.
            var carrier = factory.Argument.Split('.').Last();
            Assert.DoesNotMatch(
                new Regex($@"\bstring\??\s+{Regex.Escape(carrier)}\b"),
                factory.Text);
        }

        // (3) No application contract turns display text into an identity.
        var contracts = new[]
        {
            typeof(IJobOnService), typeof(IJobOnRepository), typeof(IToolService), typeof(IToolRepository),
        };

        var identityReturns = new[] { typeof(Guid), typeof(Guid?), typeof(ToolId), typeof(JobOnId) };
        foreach (var contract in contracts)
        {
            foreach (var method in contract.GetMethods())
            {
                var displayTextParameters = method.GetParameters().Where(IsDisplayTextParameter).ToList();
                var returned = Unwrap(method.ReturnType);

                Assert.False(
                    displayTextParameters.Count > 0 && identityReturns.Contains(returned),
                    $"{contract.Name}.{method.Name} derives an identity from display text.");
                Assert.False(
                    displayTextParameters.Count >= 3,
                    $"{contract.Name}.{method.Name} accepts a reference, a lot and a machine together.");
            }
        }

        // The one lookup that does take a reference and a lot returns the canonical Tool read model —
        // the duplicate-prevention read of the identity tuple — never a bare identity.
        var referenceAndLot = contracts
            .SelectMany(contract => contract.GetMethods())
            .Where(method => method.GetParameters().Count(IsDisplayTextParameter) >= 2)
            .ToList();
        var lookup = Assert.Single(referenceAndLot);
        Assert.Equal("FindByIdentityAsync", lookup.Name);
        Assert.Equal(typeof(Tool), Unwrap(lookup.ReturnType));

        // The identity is allocated, never derived: the only identity producers allocate.
        Assert.Contains("JobOnId.New()", ReadSource("src/DMO.Application/JobOn/JobOnService.cs"), StringComparison.Ordinal);
        Assert.Contains("ToolId.New()", ReadSource("src/DMO.Application/Tools/ToolService.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// JOB22 — N matching occurrences map to N result items with no merging, de-duplication,
    /// grouping or ranking: the count and the supplied order are preserved, including for two rows
    /// that share a production number, and no rank/score member exists. Proves AC-44.
    /// </summary>
    [Fact]
    public void JOB22_NMatchingOccurrencesMapToNItemsInTheSuppliedOrder()
    {
        // Two rows deliberately share the reference/production-number pair while owning different
        // identities: any merging, de-duplication or "latest" rule would collapse them.
        var productions = new List<JobOnProductionListItem>
        {
            new(Guid.NewGuid(), "REF-1", "100", "B1", new DateOnly(2026, 1, 5)),
            new(Guid.NewGuid(), "REF-1", "100", "B1", new DateOnly(2026, 2, 5)),
            new(Guid.NewGuid(), "REF-1", "101", "B2", null),
            new(Guid.NewGuid(), "REF-1", "099", "C3", new DateOnly(2025, 12, 1)),
        };

        var found = new JobOnResult.ProductionsFound(productions);
        Assert.Equal(productions.Count, found.Productions.Count);
        Assert.Equal(
            productions.Select(production => production.JobOnId),
            found.Productions.Select(production => production.JobOnId));

        var mapped = productions.Select(ToListItemResponse).ToList();
        Assert.Equal(productions.Count, mapped.Count);
        Assert.Equal(
            productions.Select(production => production.JobOnId),
            mapped.Select(item => item.JobonId));
        Assert.Equal(
            productions.Select(production => (production.Reference, production.ProductionNumber, production.Machine, production.ProductionDate)),
            mapped.Select(item => (item.Reference, item.ProductionNumber, item.Machine, item.ProductionDate)));

        // No ranking, scoring, recency or default-selection member exists on either carrier.
        string[] ranking = ["rank", "score", "relevance", "latest", "newest", "recent", "priority", "weight", "best", "selected", "default"];
        Assert.Empty(Offenders(PropertyMap(typeof(JobOnProductionListItem)).Keys, ranking));
        Assert.Empty(Offenders(PropertyMap(typeof(JobOnResult.ProductionsFound)).Keys, ranking));
        Assert.Empty(Offenders(PropertyMap(typeof(JobOnEndpoints.JobOnProductionListResponse)).Keys, ranking));
        Assert.Equal(5, typeof(JobOnProductionListItem).GetProperties(DeclaredInstance).Length);
    }

    // ---------------------------------------------------------------------------------------------
    // 20.3 Contexts and frozen snapshot
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// CTX9 — the context record and its frozen snapshot expose no quantity, <c>processo</c>,
    /// operational note, Baffle/calote, measurement or Boquilhas member: the frozen set is exactly the
    /// type/reference/lot triple. Proves AC-33.
    /// </summary>
    [Fact]
    public void CTX9_TheContextAndItsFrozenSnapshotCarryNoExcludedFact()
    {
        var context = PropertyMap(typeof(ToolContext));
        Assert.Equal(
            new[] { "ContextId", "ContextType", "Frozen", "JobOnId", "ToolId" },
            context.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(typeof(Guid), context["ContextId"]);
        Assert.Equal(typeof(ToolContextType), context["ContextType"]);
        Assert.Equal(typeof(JobOnId), context["JobOnId"]);
        Assert.Equal(typeof(ToolId), context["ToolId"]);
        Assert.Equal(typeof(ToolContextSnapshot), context["Frozen"]);

        // The frozen set is exactly the triple and nothing else (contract §7.2).
        var frozen = PropertyMap(typeof(ToolContextSnapshot));
        Assert.Equal(
            new[] { "Lot", "Reference", "Type" },
            frozen.Keys.OrderBy(name => name, StringComparer.Ordinal));

        string[] excluded =
        [
            "quantity", "quantidade", "processo", "note", "nota", "observ", "baffle", "calote",
            "measure", "medic", "boquilha", "status", "state", "stock", "machine",
        ];

        var scanned = new[]
        {
            typeof(ToolContext), typeof(ToolContextSnapshot), typeof(ToolContextFicha),
            typeof(CmContextEntity), typeof(MfContextEntity), typeof(BqContextEntity),
        };

        foreach (var type in scanned)
        {
            Assert.Empty(Offenders(PropertyMap(type).Keys, excluded));
            Assert.Empty(Offenders(ContractMemberNames(type), excluded));
        }

        // The context as read also exposes the frozen triple and the Tool's own live projection as two
        // separate fact classes, never a merged context fact.
        var ficha = PropertyMap(typeof(ToolContextFicha));
        Assert.Equal(
            new[] { "ContextId", "ContextType", "Tool", "ToolId", "ToolLot", "ToolReference", "ToolType" },
            ficha.Keys.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// CTX11 — no Tool or Job On type exposes a reverse collection member and no repository member
    /// returns a stored array: the only reverse read is the Tool usage query. Proves AC-38.
    /// </summary>
    [Fact]
    public void CTX11_NoToolOrJobOnTypeExposesAReverseCollectionMember()
    {
        var jobOnOwned = new[]
        {
            typeof(DomainJobOn), typeof(JobOnId), typeof(ToolContext), typeof(ToolContextSnapshot),
            typeof(ToolUsageOccurrence), typeof(JobOnEntity), typeof(CmContextEntity),
            typeof(MfContextEntity), typeof(BqContextEntity),
        };

        // Every Tool-side domain type: a collection of Job On-owned things would be the forbidden
        // reverse array.
        var toolDomainTypes = typeof(Tool).Assembly.GetTypes()
            .Where(type => type.Namespace == "DMO.Domain.Tools")
            .ToList();
        Assert.Contains(typeof(Tool), toolDomainTypes);
        Assert.Contains(typeof(ToolContext), toolDomainTypes);

        var reverseCollections = toolDomainTypes
            .SelectMany(type => DeclaredMemberTypes(type).Select(member => (Type: type, Member: member)))
            .Where(entry => IsCollectionOf(entry.Member, jobOnOwned))
            .ToList();
        Assert.Empty(reverseCollections);

        // The persistence entities agree: no Tool row holds a collection of occurrences or contexts.
        var entityReverseCollections = P2T04Entities
            .SelectMany(type => DeclaredMemberTypes(type).Select(member => (Type: type, Member: member)))
            .Where(entry => IsCollectionOf(entry.Member, jobOnOwned))
            .ToList();
        Assert.Empty(entityReverseCollections);

        // No Tool-side member is even named like a reverse navigation.
        var toolSideNames = new[] { typeof(Tool), typeof(ToolEntity), typeof(ToolMachineEntity) }
            .SelectMany(ContractMemberNames)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.Empty(Offenders(toolSideNames, ["jobon", "usage", "occurrence", "context"]));

        // The registry contract exposes no stored array: one reverse read, and it is a query.
        Assert.Empty(typeof(IToolRepository).GetProperties());
        var reverseReads = typeof(IToolRepository).GetMethods()
            .Where(method => IsCollectionOf(Unwrap(method.ReturnType), jobOnOwned))
            .ToList();
        var reverseRead = Assert.Single(reverseReads);
        Assert.Equal(nameof(IToolRepository.ListUsageOccurrencesAsync), reverseRead.Name);
        Assert.Equal(typeof(ToolUsageOccurrence), ElementTypeOf(Unwrap(reverseRead.ReturnType)));
        Assert.Equal(
            new[] { typeof(Guid), typeof(CancellationToken) },
            reverseRead.GetParameters().Select(parameter => parameter.ParameterType));
    }

    /// <summary>
    /// CTX16 — context resolution by (job on, type) is at most one context: the Job On API exposes no
    /// list-of-contexts-of-a-type operation, and the resolution the service performs is scalar.
    /// Proves AC-29.
    /// </summary>
    [Fact]
    public void CTX16_ContextResolutionIsAtMostOnePerTypeWithNoListOperation()
    {
        foreach (var contract in new[] { typeof(IJobOnRepository), typeof(IJobOnService) })
        {
            foreach (var method in contract.GetMethods())
            {
                Assert.DoesNotContain("context", method.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    method.GetParameters(),
                    parameter => parameter.ParameterType == typeof(ToolContextType));

                var element = ElementTypeOf(Unwrap(method.ReturnType));
                Assert.False(
                    element == typeof(ToolContext) || element == typeof(ToolContextFicha),
                    $"{contract.Name}.{method.Name} would list the contexts of an occurrence.");
            }
        }

        // No type of the Job On area carries a per-type context collection either.
        var perTypeCollections = JobOnContractTypes()
            .SelectMany(type => DeclaredMemberTypes(type).Select(member => (Type: type, Member: member)))
            .Where(entry => IsDictionaryKeyedBy(entry.Member, typeof(ToolContextType)))
            .ToList();
        Assert.Empty(perTypeCollections);

        // The (occurrence, type) resolution the delete path performs is scalar: one nullable id.
        var resolver = Assert.Single(
            typeof(JobOnService).GetMethods(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly),
            method => method.Name.Contains("ContextId", StringComparison.Ordinal));
        Assert.Equal(typeof(Guid?), resolver.ReturnType);
        Assert.Equal(
            new[] { typeof(DomainJobOn), typeof(ToolContextType) },
            resolver.GetParameters().Select(parameter => parameter.ParameterType));

        // The same scalar shape is offered to every dependency probe and to the edit surface.
        var target = PropertyMap(typeof(JobOnDependencyTarget));
        Assert.Equal(
            new[] { "BqContextId", "CmContextId", "JobOnId", "MfContextId" },
            target.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(
            new[] { "CmContextId", "MfContextId", "BqContextId" },
            name => Assert.Equal(typeof(Guid?), target[name]));
        Assert.Equal(typeof(Guid?), typeof(JobOnEditSlotRegion).GetProperty("ContextId")!.PropertyType);

        // The ficha carries one entry per existing context, each with its own single context identity.
        Assert.Equal(typeof(IReadOnlyList<ToolContextFicha>), typeof(JobOnFicha).GetProperty("Contexts")!.PropertyType);
        var contextIdentity = Assert.Single(
            typeof(ToolContextFicha).GetProperties(DeclaredInstance),
            property => property.Name == "ContextId");
        Assert.Equal(typeof(Guid), contextIdentity.PropertyType);
    }

    // ---------------------------------------------------------------------------------------------
    // 20.4 Duplication
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// DUP13 — no duplication member, default or query selects a source implicitly: the source is a
    /// required parameter and no "latest"/"previous" concept exists. Proves AC-55.
    /// </summary>
    [Fact]
    public void DUP13_NoDuplicationMemberOrDefaultSelectsASourceImplicitly()
    {
        string[] implicitSource = ["latest", "previous", "automatic", "default", "recent", "newest", "implicit"];

        var carriers = new[] { typeof(DuplicateJobOnCommand), typeof(JobOnResult.DuplicationPreview), typeof(IJobOnService) };
        var names = carriers.SelectMany(ContractMemberNames).Distinct(StringComparer.Ordinal).ToList();
        Assert.Empty(Offenders(names, implicitSource));

        // The command's source is its first, required, non-defaulted member.
        var constructor = Assert.Single(typeof(DuplicateJobOnCommand).GetConstructors());
        Assert.Equal("SourceJobOnId", constructor.GetParameters()[0].Name);
        Assert.Equal(typeof(Guid), constructor.GetParameters()[0].ParameterType);
        Assert.All(constructor.GetParameters(), parameter => Assert.False(parameter.HasDefaultValue));

        // Both duplication entry points require the source explicitly, and there is exactly one of each.
        // Per contract §12.2 the preview takes the source id; the duplication itself takes the command,
        // whose first, required, non-defaulted member is the source id (asserted above).
        foreach (var member in new[] { "DuplicateAsync", "PreviewDuplicateAsync" })
        {
            var method = Assert.Single(typeof(IJobOnService).GetMethods(), candidate => candidate.Name == member);
            var source = method.GetParameters()[0];
            if (member == "PreviewDuplicateAsync")
            {
                Assert.Equal(typeof(Guid), source.ParameterType);
                Assert.Contains("source", source.Name!, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                Assert.Equal(typeof(DuplicateJobOnCommand), source.ParameterType);
                Assert.Equal("command", source.Name);
            }

            Assert.False(source.HasDefaultValue);
        }

        // Nothing else in the service contract could return "the" source occurrence. The count is
        // 8 since P2-T05: the accepted Q-CAND additive member ListPesoAssociationCandidatesAsync
        // (P2-T05 contract §20.4.2, Architect ACCEPT) is the single cross-stream additive read-only
        // method on the Job On application contract; DUP13's closed-surface guarantee is unchanged
        // for the duplication members themselves.
        Assert.Equal(8, typeof(IJobOnService).GetMethods().Length);
    }

    /// <summary>
    /// DUP16 — the duplication request carrier contains no source context id and no client-driven
    /// <c>tool_id</c> reuse field: it carries the previewed source version and the new occurrence's own
    /// facts only. Proves AC-58.
    /// </summary>
    [Fact]
    public void DUP16_TheDuplicationRequestCarriesNoSourceContextOrToolId()
    {
        var request = PropertyMap(typeof(JobOnEndpoints.DuplicateJobOnRequest));
        Assert.Equal(
            new[] { "ExpectedSourceVersion", "Machine", "ProductionDate", "ProductionNumber" },
            request.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(typeof(int), request["ExpectedSourceVersion"]);
        Assert.Equal(typeof(string), request["ProductionNumber"]);
        Assert.Equal(typeof(string), request["Machine"]);
        Assert.Equal(typeof(DateOnly?), request["ProductionDate"]);

        // The carrier transports no identity value at all, so no client-supplied context id, no
        // copied-context id and no Tool id can reach the duplication.
        Assert.DoesNotContain(
            typeof(JobOnEndpoints.DuplicateJobOnRequest).GetProperties(DeclaredInstance),
            property => property.PropertyType == typeof(Guid) || property.PropertyType == typeof(Guid?));

        string[] foreignCarriers = ["context", "toolid", "cmid", "mfid", "bqid", "cmtool", "mftool", "bqtool", "copied", "sourceid"];
        Assert.Empty(Offenders(request.Keys, foreignCarriers));

        // Every member is required: a client cannot omit a fact or add one.
        var parameters = typeof(JobOnEndpoints.DuplicateJobOnRequest).GetConstructors().Single().GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.All(parameters, parameter => Assert.False(parameter.HasDefaultValue));

        // The explicit source travels in the route, bound by the server, never in the body.
        Assert.Contains(
            "MapPost(\"/{jobonId:guid}/duplicate\"",
            ReadSource("src/DMO.Web/Endpoints/JobOnEndpoints.cs"),
            StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // 20.7 Tool orchestration and origin-state boundary
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ORC3 — no shared-consumer type, member, constant or DOM hook declares an opaque key as
    /// <c>tool_id</c>/<c>jobon_id</c>/<c>cm_id</c>/<c>mf_id</c>/<c>bq_id</c>: the opaque keys stay
    /// opaque, and the canonical identity is a separate carrier the adapter resolves. Proves AC-49.
    /// </summary>
    [Fact]
    public void ORC3_NoOpaqueKeyIsDeclaredAsACanonicalIdentity()
    {
        var sources = ConsumerSources();
        Assert.NotEmpty(sources);

        var tokens = sources
            .SelectMany(file => Regex.Matches(file.Text, "data-dmo-[a-z0-9-]+").Select(match => match.Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // The opaque key hooks of the P2-T04 consumer surfaces are exactly these, and the rendered
        // shared summary row's ItemKey is one of them.
        var keyHooks = tokens.Where(token => token.EndsWith("-key", StringComparison.Ordinal)).OrderBy(token => token, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "data-dmo-candidate-key", "data-dmo-item-key" }, keyHooks);

        var routeHooks = tokens.Where(token => token.Contains("route", StringComparison.Ordinal)).OrderBy(token => token, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "data-dmo-jobon-route", "data-dmo-jobon-route-map" }, routeHooks);

        // No key or route hook is even spelled as a canonical identity.
        foreach (var hook in keyHooks.Concat(routeHooks))
        {
            var normalized = hook.Replace("-", string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("toolid", normalized, StringComparison.Ordinal);
            Assert.DoesNotContain("jobonid", normalized, StringComparison.Ordinal);
            Assert.DoesNotContain("cmid", normalized, StringComparison.Ordinal);
            Assert.DoesNotContain("mfid", normalized, StringComparison.Ordinal);
            Assert.DoesNotContain("bqid", normalized, StringComparison.Ordinal);
        }

        // No line that declares or reads an opaque key/route hook is fed by a canonical identity.
        string[] identitySpellings = ["ToolId", "JobOnId", "CmId", "MfId", "BqId", "Guid"];
        foreach (var file in sources)
        {
            foreach (var line in file.Text.Split('\n'))
            {
                if (!keyHooks.Concat(routeHooks).Any(hook => line.Contains(hook, StringComparison.Ordinal)))
                {
                    continue;
                }

                Assert.DoesNotContain(identitySpellings, spelling => line.Contains(spelling, StringComparison.Ordinal));
            }
        }

        // The opaque key and the canonical identity are two distinct carriers of the same map element.
        var mapLines = sources
            .SelectMany(file => file.Text.Split('\n'))
            .Where(line => line.Contains("data-dmo-jobon-candidate=\"@candidate.Key\"", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(mapLines);
        Assert.All(
            mapLines,
            line => Assert.Contains("data-dmo-jobon-candidate-tool=\"@candidate.ToolId\"", line, StringComparison.Ordinal));

        // The carriers themselves: an opaque string key beside a canonical Guid identity.
        Assert.Equal(2, typeof(ToolCandidateEntry).GetProperties(DeclaredInstance).Length);
        Assert.Equal(typeof(string), typeof(ToolCandidateEntry).GetProperty("Key")!.PropertyType);
        Assert.Equal(typeof(Guid), typeof(ToolCandidateEntry).GetProperty("ToolId")!.PropertyType);
        Assert.Equal(typeof(string), typeof(JobOnRouteEntry).GetProperty("Key")!.PropertyType);
        Assert.Equal(typeof(string), typeof(JobOnRouteEntry).GetProperty("Href")!.PropertyType);
        Assert.Equal(typeof(string), typeof(ToolSummaryRowPresentation).GetProperty("ItemKey")!.PropertyType);

        // The P2-T04 consumer supplies an opaque item key to the shared row, never an identity.
        var viewSource = ReadSource("src/DMO.Web/Pages/JobOn/View.cshtml.cs");
        Assert.Contains("$\"context-{token}\"", viewSource, StringComparison.Ordinal);

        // Behaviour: the key is generated by the adapter, is not a Guid, and resolves an identity only
        // through the adapter's own map — display text never resolves anything.
        var toolId = Guid.NewGuid();
        var item = new ToolSearchItem(toolId, ToolType.Cm, "5447T173", "12", Processo.Nnpb, 4, [MachineCode.From("B1")]);
        var adapter = new JobOnToolPickerAdapter(ToolContextType.Cm, [item]);

        var candidate = Assert.Single(adapter.Candidates);
        Assert.StartsWith(JobOnToolPickerAdapter.CandidateKeyPrefix, candidate.Key, StringComparison.Ordinal);
        Assert.False(Guid.TryParse(candidate.Key, out _), "The candidate key must never be a canonical identity.");

        var entry = Assert.Single(adapter.CandidateMap);
        Assert.Equal(candidate.Key, entry.Key);
        Assert.Equal(toolId, entry.ToolId);
        Assert.Equal(toolId, adapter.ResolveCandidate(candidate.Key));

        // Not a reference, not a lot, not a machine and not the identity's own text form.
        Assert.Null(adapter.ResolveCandidate(item.Reference));
        Assert.Null(adapter.ResolveCandidate(item.Lot));
        Assert.Null(adapter.ResolveCandidate(item.CompatibleMachines[0].Value));
        Assert.Null(adapter.ResolveCandidate(toolId.ToString()));
    }

    /// <summary>
    /// ORC7 — no P2-T04 type writes <c>tools</c>/<c>tool_machines</c> outside the Tool create
    /// transaction, and the Job On area owns no Tool master fact: it never references the Tool entity
    /// or the Tool repository. Proves AC-53.
    /// </summary>
    [Fact]
    public void ORC7_OnlyTheToolCreateTransactionWritesTheToolTables()
    {
        var sources = AllProductionSources();
        Assert.NotEmpty(sources);

        string[] toolWrites = ["Set<ToolEntity>().Add", "Add(new ToolEntity", "Set<ToolMachineEntity>().Add", "Add(new ToolMachineEntity"];

        var writers = sources
            .Where(file => toolWrites.Any(pattern => file.Text.Contains(pattern, StringComparison.Ordinal)))
            .Select(file => file.RelativePath)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(new[] { "src/DMO.Infrastructure/Persistence/ToolRepository.cs" }, writers);

        // Both inserts happen inside the one Tool create transaction.
        var repository = ReadSource("src/DMO.Infrastructure/Persistence/ToolRepository.cs");
        var createStart = repository.IndexOf("public async Task<Tool> CreatedAsync", StringComparison.Ordinal);
        var createEnd = repository.IndexOf("public async Task<IReadOnlyList<ToolUsageOccurrence>> ListUsageOccurrencesAsync", StringComparison.Ordinal);
        Assert.True(createStart >= 0 && createEnd > createStart, "The Tool create transaction must exist.");

        var create = repository[createStart..createEnd];
        Assert.Contains("_context.Set<ToolEntity>().Add(entity)", create, StringComparison.Ordinal);
        Assert.Contains("_context.Set<ToolMachineEntity>().Add(new ToolMachineEntity", create, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync", create, StringComparison.Ordinal);
        Assert.Contains("CommitAsync", create, StringComparison.Ordinal);

        // No other production source creates, updates or deletes those rows: the Tool is immutable and
        // a referenced Tool cannot be deleted.
        string[] forbiddenWrites =
        [
            "new ToolEntity", "new ToolMachineEntity", "Set<ToolEntity>().Remove", "Set<ToolEntity>().Update",
            "Set<ToolMachineEntity>().Remove", "Set<ToolMachineEntity>().Update",
        ];
        foreach (var pattern in forbiddenWrites)
        {
            var offenders = sources
                .Where(file => file.Text.Contains(pattern, StringComparison.Ordinal))
                .Where(file => !file.RelativePath.EndsWith("ToolRepository.cs", StringComparison.Ordinal))
                .Select(file => file.RelativePath)
                .ToList();
            Assert.Empty(offenders);
        }

        // The Web surfaces (pages and endpoints) never touch the Tool master data directly.
        foreach (var file in WebSources())
        {
            Assert.DoesNotContain("ToolEntity", file.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("ToolMachineEntity", file.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("IToolRepository", file.Text, StringComparison.Ordinal);
        }

        // The Job On area owns no Tool master fact and never writes the Tool registry: it only reads
        // canonical Tools (existence and type match) through its own service state.
        foreach (var file in SourcesUnder("src/DMO.Application/JobOn").Concat(SourcesUnder("src/DMO.Web/Pages/JobOn")))
        {
            Assert.DoesNotContain("ToolEntity", file.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("ToolMachineEntity", file.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("_tools.CreatedAsync", file.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("tool_machines", file.Text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// ORC8 — no standalone Tool create page, route or server-side draft/store table exists, and the
    /// Tool create subflow is issued from the origin surface. Proves AC-54.
    /// </summary>
    [Fact]
    public void ORC8_NoStandaloneToolCreatePageRouteOrDraftStoreExists()
    {
        var root = RepositoryRoot();
        Assert.False(File.Exists(Path.Combine(root, "src", "DMO.Web", "Pages", "Ferramentas", "Create.cshtml")));
        Assert.False(File.Exists(Path.Combine(root, "src", "DMO.Web", "Pages", "Ferramentas", "Create.cshtml.cs")));

        // The contextual Ferramentas surface has exactly the search/list route and the create API the
        // origin surface posts to: no create page route exists.
        var ferramentas = ReadSource("src/DMO.Web/Endpoints/FerramentasEndpoints.cs");
        Assert.Contains("FerramentasBasePath = \"/ferramentas\"", ferramentas, StringComparison.Ordinal);

        var ferramentasTemplates = RouteTemplates(ferramentas);
        Assert.Equal(new[] { "/tools", "/tools" }, ferramentasTemplates);

        foreach (var file in AllProductionSources())
        {
            Assert.DoesNotContain("/tools/create", file.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("tools/create", file.Text, StringComparison.Ordinal);
        }

        // The schema owns exactly the six contracted tables: there is no draft, staging or store table.
        var migration = ReadSource(NewMigrationPath());
        var tables = Regex.Matches(migration, "CreateTable\\(\\s*name: \"(?<name>[^\"]+)\"")
            .Select(match => match.Groups["name"].Value)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(ContractedTableNames, tables);

        string[] draftVocabulary = ["draft", "store", "staging", "scratch", "cache", "pending", "provisional", "session", "workspace"];
        foreach (var table in tables)
        {
            Assert.Empty(Offenders(new[] { table }, draftVocabulary));
        }

        // The persistence context exposes the foundation sets only: nothing holds a Tool draft.
        var dbSets = typeof(DmoDbContext)
            .GetProperties(DeclaredInstance)
            .Where(property => property.PropertyType.IsGenericType &&
                               property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(new[] { "AdminAccounts", "TemplateModules", "Templates", "Users" }, dbSets);
        Assert.Empty(Offenders(dbSets, draftVocabulary));

        // The subflow is issued from the origin surface: the script posts to the contextual endpoint,
        // stays on the page and writes the returned canonical identity into the origin slot.
        var script = ReadSource("src/DMO.Web/wwwroot/js/dmo-jobon.js");
        Assert.Contains("var TOOL_CREATE_ENDPOINT = '/ferramentas/tools';", script, StringComparison.Ordinal);
        Assert.Contains("fetch(TOOL_CREATE_ENDPOINT, {", script, StringComparison.Ordinal);
        Assert.Contains("method: 'POST'", script, StringComparison.Ordinal);
        Assert.Contains("hidden.value = result.body.toolId;", script, StringComparison.Ordinal);

        foreach (var page in new[] { "Create.cshtml", "Edit.cshtml" })
        {
            var markup = ReadSource($"src/DMO.Web/Pages/JobOn/{page}");
            Assert.Contains("data-dmo-jobon-tool-create-panel=\"true\"", markup, StringComparison.Ordinal);
            Assert.Contains("data-dmo-jobon-tool-create-submit=\"true\"", markup, StringComparison.Ordinal);
            Assert.Contains("data-dmo-jobon-tool-create-cancel=\"true\"", markup, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // 20.5 Delete and dependency rule
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// DEP1 — every P2-T04 entity configuration declares <c>DeleteBehavior.Restrict</c> for every
    /// foreign key: the built model carries exactly the eight contracted RESTRICT foreign keys and no
    /// cascade anywhere. Proves AC-80.
    /// </summary>
    [Fact]
    public void DEP1_EveryP2T04EntityConfigurationDeclaresRestrictForEveryForeignKey()
    {
        // The six configurations exist, are concrete and are typed for the six contracted entities.
        var configurations = typeof(DmoDbContext).Assembly.GetTypes()
            .Where(type => type.Namespace == "DMO.Infrastructure.Persistence.EntityConfigurations")
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.GetInterfaces().Any(@interface =>
                @interface.IsGenericType &&
                @interface.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>) &&
                P2T04Entities.Contains(@interface.GetGenericArguments()[0])))
            .ToList();

        Assert.Equal(6, configurations.Count);
        foreach (var configuration in configurations)
        {
            Assert.NotNull(Activator.CreateInstance(configuration));
        }

        var model = BuiltModel();

        // The contracted foreign keys per entity: the Tool is the principal (no foreign key of its
        // own), the compatibility row points at the Tool, the occurrence points at itself for the
        // recorded lineage, and each context points at the occurrence and at the canonical Tool.
        var expectedForeignKeys = new Dictionary<Type, int>
        {
            [typeof(ToolEntity)] = 0,
            [typeof(ToolMachineEntity)] = 1,
            [typeof(JobOnEntity)] = 1,
            [typeof(CmContextEntity)] = 2,
            [typeof(MfContextEntity)] = 2,
            [typeof(BqContextEntity)] = 2,
        };

        foreach (var (entity, expected) in expectedForeignKeys)
        {
            var entityType = model.FindEntityType(entity);
            Assert.NotNull(entityType);

            var entityForeignKeyList = entityType!.GetForeignKeys();
            Assert.Equal(expected, entityForeignKeyList.Count());
            Assert.All(entityForeignKeyList, foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
        }

        // The contracted set: the lineage self-FK, the three context → occurrence FKs, the three
        // context → Tool FKs and the compatibility → Tool FK.
        var foreignKeys = P2T04Entities
            .SelectMany(entity => model.FindEntityType(entity)!.GetForeignKeys())
            .ToList();
        Assert.Equal(8, foreignKeys.Count);
        Assert.All(foreignKeys, foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
    }

    /// <summary>
    /// DEP11 — no soft-delete, archive or cancel column, flag or state exists in the schema or in the
    /// domain types: a Job On is either present or gone. Proves AC-80 and AC-98.
    /// </summary>
    [Fact]
    public void DEP11_NoSoftDeleteArchiveOrCancelColumnFlagOrStateExists()
    {
        string[] softDeleteVocabulary =
        [
            "deleted", "archived", "isactive", "inactive", "cancelled", "canceled", "softdelete",
            "voided", "state", "lifecycle", "status",
        ];

        // Domain types.
        foreach (var type in new[] { typeof(DomainJobOn), typeof(JobOnId), typeof(ToolContext), typeof(ToolContextSnapshot) })
        {
            Assert.Empty(Offenders(PropertyMap(type).Keys, softDeleteVocabulary));
            Assert.Empty(Offenders(ContractMemberNames(type), softDeleteVocabulary));
        }

        // Schema: the built model owns exactly the contracted columns of the six tables.
        var model = BuiltModel();
        foreach (var entity in P2T04Entities)
        {
            var entityType = model.FindEntityType(entity);
            Assert.NotNull(entityType);

            var columns = entityType!.GetProperties().Select(property => property.Name).ToList();
            Assert.NotEmpty(columns);
            Assert.Empty(Offenders(columns, softDeleteVocabulary));
        }

        // The occurrence's own checks constrain the facts, never a flag.
        var jobOnChecks = model.FindEntityType(typeof(JobOnEntity))!
            .GetCheckConstraints()
            .Select(constraint => constraint.Name!)
            .ToList();
        Assert.Equal(
            new[] { "job_ons_machine_check", "job_ons_production_number_required_check", "job_ons_reference_required_check" },
            jobOnChecks.OrderBy(name => name, StringComparer.Ordinal));

        // The DDL agrees: no soft-delete vocabulary anywhere in the migration.
        var migration = ReadSource(NewMigrationPath());
        Assert.Empty(Offenders(new[] { migration }, softDeleteVocabulary));
    }

    /// <summary>
    /// DEP12 — the delete API exposes no cascade, force or recursive option: the delete command is the
    /// confirmation pair for one occurrence, the route takes one occurrence id, and no member accepts a
    /// set of occurrences. Proves AC-80.
    /// </summary>
    [Fact]
    public void DEP12_TheDeleteApiExposesNoCascadeForceOrRecursiveOption()
    {
        string[] vocabulary = ["cascade", "force", "recursive", "recurse", "harddelete", "purge", "wipe", "truncate", "bulk", "batch"];

        var contracts = new[]
        {
            typeof(IJobOnService), typeof(IJobOnRepository), typeof(DeleteJobOnCommand), typeof(JobOnEndpoints),
        };
        foreach (var contract in contracts)
        {
            Assert.Empty(Offenders(ContractMemberNames(contract), vocabulary));
        }

        // The command is exactly the confirmation pair for one occurrence.
        var command = PropertyMap(typeof(DeleteJobOnCommand));
        Assert.Equal(
            new[] { "DateThresholdWarningAcknowledged", "DeleteConfirmed", "ExpectedVersion", "JobOnId" },
            command.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(typeof(Guid), command["JobOnId"]);
        Assert.Equal(typeof(int), command["ExpectedVersion"]);
        Assert.Equal(typeof(bool), command["DeleteConfirmed"]);
        Assert.Equal(typeof(bool), command["DateThresholdWarningAcknowledged"]);

        // Neither delete member accepts a collection: one occurrence per call, never a set.
        var deleteMembers = new[]
        {
            Assert.Single(typeof(IJobOnService).GetMethods(), method => method.Name == nameof(IJobOnService.DeleteAsync)),
            Assert.Single(typeof(IJobOnRepository).GetMethods(), method => method.Name == nameof(IJobOnRepository.DeletedAsync)),
        };
        foreach (var member in deleteMembers)
        {
            Assert.All(
                member.GetParameters(),
                parameter => Assert.False(
                    IsCollection(parameter.ParameterType),
                    $"{member.Name} must not accept a set of occurrences."));
        }

        // The route table agrees: the only route parameters in the whole Job On surface are the
        // occurrence id and — on the additive Peso PDF open route of the outputs slice — the Peso
        // id; no template mentions a cascade-style option.
        var templates = JobOnRouteTemplates();
        Assert.Contains("/{jobonId:guid}", templates);
        Assert.Empty(Offenders(templates, vocabulary));

        var routeParameters = templates
            .SelectMany(template => Regex.Matches(template, "\\{(?<parameter>[^}]+)\\}").Select(match => match.Groups["parameter"].Value))
            .Select(parameter => parameter.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(new[] { "jobonid:guid", "pesoid:guid" }, routeParameters);

        // The delete is one occurrence per call, addressed by that single route parameter.
        Assert.Contains(
            "manage.MapDelete(\"/{jobonId:guid}\"",
            ReadSource("src/DMO.Web/Endpoints/JobOnEndpoints.cs"),
            StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------------------------------

    private static readonly BindingFlags DeclaredInstance = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly BindingFlags DeclaredAll =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>One scanned source file: its repository-relative path and its full text.</summary>
    private sealed record SourceFile(string RelativePath, string Text);

    /// <summary>The Job On contract types: the domain record, the application carriers and the route carriers.</summary>
    private static IReadOnlyList<Type> JobOnContractTypes() =>
    [
        typeof(DomainJobOn),
        typeof(JobOnId),
        typeof(ToolContext),
        .. DeclaredTypesIn("DMO.Application.JobOn", typeof(JobOnResult).Assembly),
        typeof(JobOnRouteEntry),
        typeof(JobOnSlotRegion),
        typeof(JobOnEditSlotRegion),
        typeof(JobOnContextRegion),
        typeof(ToolCandidateEntry),
        typeof(JobOnSlotTokens),
        .. typeof(JobOnEndpoints).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Where(IsHandWrittenType),
        .. DeclaredTypesIn("DMO.Web.Pages.JobOn", typeof(IndexModel).Assembly),
    ];

    /// <summary>
    /// The hand-written types declared in one namespace: compiler-generated helpers (closure classes
    /// and async state machines) are not contract types and are excluded.
    /// </summary>
    private static IReadOnlyList<Type> DeclaredTypesIn(string @namespace, Assembly assembly) =>
        assembly.GetTypes()
            .Where(type => type.Namespace == @namespace)
            .Where(IsHandWrittenType)
            .ToList();

    /// <summary>Whether a type is hand-written source rather than a compiler artifact.</summary>
    private static bool IsHandWrittenType(Type type) => !type.Name.Contains('<');

    /// <summary>The declared route table of the Job On surface: endpoint templates and page routes.</summary>
    private static IReadOnlyList<string> JobOnRouteTemplates()
    {
        var endpoint = ReadSource("src/DMO.Web/Endpoints/JobOnEndpoints.cs");
        var templates = RouteTemplates(endpoint).ToList();

        var basePath = Regex.Match(endpoint, "JobOnBasePath\\s*=\\s*\"(?<path>[^\"]+)\"");
        Assert.True(basePath.Success, "The Job On base path must be declared in the route table source.");
        templates.Add(basePath.Groups["path"].Value);

        foreach (var page in Directory.GetFiles(
                     Path.Combine(RepositoryRoot(), "src", "DMO.Web", "Pages", "JobOn"),
                     "*.cshtml",
                     SearchOption.TopDirectoryOnly))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(page), "@page\\s+\"(?<template>[^\"]+)\""))
            {
                templates.Add(match.Groups["template"].Value);
            }
        }

        return templates;
    }

    /// <summary>The route templates declared by a minimal-API source.</summary>
    private static IReadOnlyList<string> RouteTemplates(string source) =>
        Regex.Matches(source, "Map(?:Get|Post|Put|Delete|Methods)\\(\\s*\"(?<template>[^\"]*)\"")
            .Select(match => match.Groups["template"].Value)
            .ToList();

    /// <summary>The compiled EF model of the production context (no connection is opened).</summary>
    private static IModel BuiltModel()
    {
        var options = new DbContextOptionsBuilder<DmoDbContext>()
            .UseNpgsql(PlaceholderConnectionString)
            .Options;

        using var context = new DmoDbContext(options);

        return context.GetService<IDesignTimeModel>().Model;
    }

    /// <summary>Every declared member name of a type, excluding compiler-generated artifacts.</summary>
    private static IReadOnlyList<string> ContractMemberNames(Type type) =>
        type.GetMembers(DeclaredAll)
            .Where(member => !IsCompilerGenerated(member))
            .Select(member => member.Name)
            .ToList();

    /// <summary>Every declared member type of a type, excluding compiler-generated artifacts.</summary>
    private static IReadOnlyList<Type> DeclaredMemberTypes(Type type) =>
        type.GetMembers(DeclaredAll)
            .Where(member => !IsCompilerGenerated(member))
            .SelectMany(member => member switch
            {
                PropertyInfo property => new[] { property.PropertyType },
                FieldInfo field => new[] { field.FieldType },
                MethodInfo method => method.GetParameters()
                    .Select(parameter => parameter.ParameterType)
                    .Append(method.ReturnType)
                    .ToArray(),
                _ => [],
            })
            .ToList();

    private static bool IsCompilerGenerated(MemberInfo member) =>
        member.Name.Contains('<') ||
        member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) ||
        (member.DeclaringType is { } declaring && declaring.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false));

    /// <summary>The declared public instance properties of a type, keyed by name.</summary>
    private static Dictionary<string, Type> PropertyMap(Type type) =>
        type.GetProperties(DeclaredInstance).ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);

    private static bool IsCollection(Type type)
    {
        if (type.IsArray)
        {
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();

        return definition == typeof(IReadOnlyList<>) ||
               definition == typeof(IList<>) ||
               definition == typeof(List<>) ||
               definition == typeof(ICollection<>) ||
               definition == typeof(IEnumerable<>) ||
               definition == typeof(IReadOnlyCollection<>);
    }

    private static Type? ElementTypeOf(Type type) =>
        type.IsArray ? type.GetElementType() : type.IsGenericType ? type.GetGenericArguments()[0] : null;

    private static bool IsCollectionOf(Type type, IReadOnlyList<Type> elementTypes) =>
        IsCollection(type) && ElementTypeOf(type) is { } element && elementTypes.Contains(element);

    private static bool IsDictionaryKeyedBy(Type type, Type key)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        var isDictionary = definition == typeof(IDictionary<,>) ||
                           definition == typeof(Dictionary<,>) ||
                           definition == typeof(IReadOnlyDictionary<,>);

        return isDictionary && type.GetGenericArguments()[0] == key;
    }

    private static Type Unwrap(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>)
            ? type.GetGenericArguments()[0]
            : type;

    private static bool IsDisplayTextParameter(ParameterInfo parameter) =>
        parameter.Name is { } name &&
        (name.Contains("reference", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("lot", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("machine", StringComparison.OrdinalIgnoreCase));

    /// <summary>Every supplied name that carries any of the forbidden tokens.</summary>
    private static IReadOnlyList<string> Offenders(IEnumerable<string> names, IEnumerable<string> tokens)
    {
        var tokenList = tokens.ToList();

        return names
            .Where(name => tokenList.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static JobOnEndpoints.JobOnProductionItemResponse ToListItemResponse(JobOnProductionListItem item) =>
        new(item.JobOnId, item.Reference, item.ProductionNumber, item.Machine, item.ProductionDate);

    /// <summary>The P2-T04 application and Job On Web sources scanned by JOB21.</summary>
    private static IReadOnlyList<SourceFile> ApplicationAndJobOnWebSources() =>
    [
        .. SourcesUnder("src/DMO.Application"),
        .. SourcesUnder("src/DMO.Web/Endpoints"),
        .. SourcesUnder("src/DMO.Web/Pages/JobOn"),
    ];

    /// <summary>Every production source in the repository.</summary>
    private static IReadOnlyList<SourceFile> AllProductionSources() => SourcesUnder("src");

    /// <summary>Every Web source: pages, endpoints and the client script.</summary>
    private static IReadOnlyList<SourceFile> WebSources()
    {
        var root = Path.Combine(RepositoryRoot(), "src", "DMO.Web");

        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) ||
                           path.EndsWith(".cshtml", StringComparison.Ordinal) ||
                           path.EndsWith(".js", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new SourceFile(RelativeToRoot(path), File.ReadAllText(path)))
            .ToList();
    }

    /// <summary>
    /// The P2-T04 shared-consumer sources scanned by ORC3: the orchestration adapter, the Job On and
    /// Ferramentas pages, the endpoints, the client script and the shared summary row it feeds.
    /// </summary>
    private static IReadOnlyList<SourceFile> ConsumerSources()
    {
        var root = Path.Combine(RepositoryRoot(), "src", "DMO.Web", "Pages", "JobOn");
        var paths = Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".cshtml", StringComparison.Ordinal))
            .Select(RelativeToRoot)
            .Concat(
            [
                "src/DMO.Web/Endpoints/JobOnEndpoints.cs",
                "src/DMO.Web/Endpoints/FerramentasEndpoints.cs",
                "src/DMO.Web/Pages/Ferramentas/Tool.cshtml",
                "src/DMO.Web/Pages/Ferramentas/Tool.cshtml.cs",
                "src/DMO.Web/Pages/Shared/Components/_ToolSummaryRow.cshtml",
                "src/DMO.Web/wwwroot/js/dmo-jobon.js",
            ])
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        return paths.Select(path => new SourceFile(path, ReadSource(path))).ToList();
    }

    /// <summary>Every C# source under one repository-relative directory.</summary>
    private static IReadOnlyList<SourceFile> SourcesUnder(string relativeDirectory)
    {
        var root = Path.Combine(RepositoryRoot(), relativeDirectory.Replace('/', Path.DirectorySeparatorChar));

        return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new SourceFile(RelativeToRoot(path), File.ReadAllText(path)))
            .ToList();
    }

    /// <summary>Whether a path is hand-written source rather than a build artifact.</summary>
    private static bool IsSourcePath(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>The single P2-T04 migration file (the EF designer companion is not source).</summary>
    private static string NewMigrationPath()
    {
        var directory = Path.Combine(RepositoryRoot(), "src", "DMO.Infrastructure", "Migrations");
        var migrations = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith("_ToolJobOnDomainCore.cs", StringComparison.Ordinal))
            .ToList();

        Assert.Single(migrations);
        return RelativeToRoot(migrations[0]);
    }

    private static string RelativeToRoot(string fullPath) =>
        Path.GetRelativePath(RepositoryRoot(), fullPath).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>The repository root, found by walking up to the directory holding <c>DMO.slnx</c>.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DMO.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    /// <summary>Reads one repository-relative source file.</summary>
    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
