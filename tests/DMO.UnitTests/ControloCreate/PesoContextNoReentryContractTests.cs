using System.Reflection;
using DMO.Application.Controlo.Pesos;
using DMO.Web.Endpoints.Controlo;

namespace DMO.UnitTests.ControloCreate;

/// <summary>
/// Owner-clarification delta (P2-T05 §31.1) — unit proof of the "no manual re-entry" contract: the
/// Peso is populated by <c>cm_id</c>/Job On with the production context (máquina, referência, lote,
/// processo, CM), so no create/calculate carrier declares ANY production-context member. The
/// operator literally cannot re-enter reference/production number/machine/lote/processo: the only
/// context input a carrier accepts is the real <c>cm_id</c> (or the truthful pending
/// <c>tool_id</c>).
/// </summary>
public sealed class PesoContextNoReentryContractTests
{
    private static readonly BindingFlags DeclaredInstance =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    /// <summary>
    /// The production-context facts that must NEVER be re-entered on a Peso carrier (identified by
    /// the exact member spellings authority forbids duplicating on the Peso).
    /// </summary>
    private static readonly string[] ForbiddenContextMembers =
    [
        "Reference",
        "ProductionNumber",
        "Machine",
        "Lot",
        "Processo",
        "FrozenToolType",
        "FrozenToolReference",
        "FrozenToolLot",
        "ProductionDate",
    ];

    /// <summary>
    /// NR1 — the create and calculate application commands and the matching transport requests
    /// carry no production-context member: the context reaches the Peso exclusively through the
    /// accepted anchor (<c>cmId</c> xor <c>pendingToolId</c>), exactly the
    /// <c>peso_id → cm_id → jobon_id + tool_id</c> chain of contract §31.1.
    /// </summary>
    [Fact]
    public void NR1_NoPesoCarrierDeclaresAProductionContextMember()
    {
        foreach (var carrier in new[]
                 {
                     typeof(CreatePesoCommand),
                     typeof(CalculatePesoCommand),
                     typeof(ControloCreateEndpoints.CreatePesoRequest),
                     typeof(ControloCreateEndpoints.CalculatePesoRequest),
                 })
        {
            var members = carrier.GetProperties(DeclaredInstance)
                .Select(property => property.Name)
                .ToArray();

            Assert.DoesNotContain(
                members,
                member => ForbiddenContextMembers.Contains(member, StringComparer.Ordinal));

            // The only context input is the accepted anchor itself.
            Assert.Contains("CmId", members, StringComparer.Ordinal);
            Assert.Contains("PendingToolId", members, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// NR2 — the published Peso sheet carries the production context as a READ projection (the
    /// <c>Production</c> traversal facts and the <c>Context</c> CM projection), never as an input.
    /// The input carriers and the output projection are different shapes: no sheet member is
    /// writable from a client carrier because every sheet member lives on read-only records.
    /// </summary>
    [Fact]
    public void NR2_ASheetPopulatesContextOnlyThroughReadProjections()
    {
        // The read model resolves the context through traversal; the input carriers hold no such
        // members (proven by NR1). This pins the sheet's context-carrying members as the ONLY
        // place the production context appears on the Peso read side — the only "reference"-spelled
        // members are the two Peso-owned optional SAP inputs, never the production reference.
        var sheetMembers = typeof(PesoSheetReadModel).GetProperties(DeclaredInstance)
            .Select(property => property.Name)
            .ToArray();

        Assert.Contains("Production", sheetMembers, StringComparer.Ordinal);
        Assert.Contains("Context", sheetMembers, StringComparer.Ordinal);
        Assert.DoesNotContain(sheetMembers, member =>
            member.Contains("Machine", StringComparison.Ordinal));

        Assert.Equal(
            new[] { "PreviousAverageWeightReference", "PreviousProductionEndReference" }.OrderBy(name => name),
            sheetMembers.Where(member => member.Contains("Reference", StringComparison.Ordinal))
                .OrderBy(name => name));
    }
}