using System.Reflection;
using DMO.Domain.ControloComparacao;

namespace DMO.UnitTests.ControloComparacao;

/// <summary>
/// The Peso Comparação identity contract: the compared CM keeps the EXACT existing <c>cm_id</c>
/// of the production context, no surrogate subject identity exists, no reading UUID exists, and
/// no previous-Peso / production / duplicated-Tool identity can be represented.
/// </summary>
/// <remarks>
/// Pin: the compared-CM identity inside one comparison event is the natural
/// <c>comparacao_id + cm_id</c>; a new comparison event allocates a new <c>comparacao_id</c>;
/// the same CM of the same production reuses the same <c>cm_id</c>; a future production with the
/// same physical Tool gets a new <c>cm_id</c> (same canonical <c>tool_id</c> — reachable through
/// <c>cm_id</c>, never duplicated).
/// </remarks>
public sealed class ComparacaoIdentityContractTests
{
    private static readonly BindingFlags DeclaredInstance =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [Fact]
    public void TheSubjectIdentityIsTheNaturalComparacaoIdPlusCmId()
    {
        // The subject is identified by the owning comparison event and the EXACT canonical CM
        // identity — nothing else is a subject identity member.
        var subjectMembers = typeof(ComparacaoCmSubject)
            .GetProperties(DeclaredInstance)
            .Select(property => property.Name)
            .ToList();

        Assert.Contains(nameof(ComparacaoCmSubject.ComparacaoId), subjectMembers);
        Assert.Contains(nameof(ComparacaoCmSubject.CmId), subjectMembers);

        // No surrogate subject identity exists anywhere in the Comparação domain surface.
        Assert.DoesNotContain(subjectMembers, name => name.Contains("SubjectId", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(ComparacaoCmSubject).Assembly.GetTypes(),
            type => type.Namespace == typeof(ComparacaoCmSubject).Namespace
                && type.Name.Contains("SubjectId", StringComparison.Ordinal));
    }

    [Fact]
    public void TheIdentityTypesAreExactlyTheComparisonEventRootAndItsChildren()
    {
        var identityTypes = typeof(ComparacaoId).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(ComparacaoId).Namespace && type.Name.EndsWith("Id", StringComparison.Ordinal))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // ONLY the comparison-event id — no subject id, no row id and no decision-event id exist.
        Assert.Equal(["ComparacaoId"], identityTypes);
    }

    [Fact]
    public void NoPreviousPesoOrProductionIdentityCanBeRepresented()
    {
        foreach (var type in new[]
                 {
                     typeof(Comparacao),
                     typeof(ComparacaoCmSubject),
                     typeof(ComparacaoMeasurementRow),
                 })
        {
            var members = type.GetProperties(DeclaredInstance).Select(property => property.Name);

            Assert.DoesNotContain(members, name => name.Contains("PreviousPeso", StringComparison.Ordinal));
            Assert.DoesNotContain(members, name => name.Contains("Production", StringComparison.Ordinal) && name != nameof(ComparacaoMeasurementRow.RowPosition));
            Assert.DoesNotContain(members, name => name.Contains("Baseline", StringComparison.Ordinal));
            Assert.DoesNotContain(members, name => name.Contains("JobOn", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void NoToolIdentityIsCopiedOrInferredFromText()
    {
        // The subject stores only the canonical cm_id; the Tool/lot/reference facts stay reachable
        // through cm_id and are never duplicated onto the comparison rows.
        foreach (var type in new[] { typeof(ComparacaoCmSubject), typeof(ComparacaoMeasurementRow) })
        {
            var members = type.GetProperties(DeclaredInstance).Select(property => property.Name);

            Assert.DoesNotContain(members, name => name.Contains("Tool", StringComparison.Ordinal));
            Assert.DoesNotContain(members, name => name.Contains("Reference", StringComparison.Ordinal));
            Assert.DoesNotContain(members, name => name.Contains("Lot", StringComparison.Ordinal));
            Assert.DoesNotContain(members, name => name.Contains("Machine", StringComparison.Ordinal));
            Assert.DoesNotContain(members, name => name.Contains("Processo", StringComparison.Ordinal));
        }
    }
}