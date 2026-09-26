using DMO.Application.ControloApprove;
using DMO.Domain.ControloComparacao;

namespace DMO.UnitTests.ControloComparacao;

/// <summary>
/// The per-CM decision vocabulary of the Peso Comparação: the stored tokens, the display labels
/// and the closed two-value shape.
/// </summary>
/// <remarks>
/// The display labels MUST bind the documented P2-T06 per-CM constants
/// (<see cref="PerCmDecisionVocabulary"/>: <c>Manter</c> / <c>Colocar de parte</c>) — the
/// vocabulary is fixed by contract and the comparison persistence binds it. The stored tokens
/// are the exact ASCII lowercase forms <c>manter</c> / <c>colocar_de_parte</c>.
/// </remarks>
public sealed class ComparacaoCmDecisionVocabularyTests
{
    [Fact]
    public void DisplayLabels_BindTheDocumentedPerCmVocabulary()
    {
        Assert.Equal(
            PerCmDecisionVocabulary.Manter,
            ComparacaoCmDecisionKindTokens.DisplayLabel(ComparacaoCmDecisionKind.Manter));
        Assert.Equal(
            PerCmDecisionVocabulary.ColocarDeParte,
            ComparacaoCmDecisionKindTokens.DisplayLabel(ComparacaoCmDecisionKind.ColocarDeParte));
    }

    [Fact]
    public void TheVocabularyIsClosedToExactlyTwoValues()
    {
        // The enum is exactly the two human decisions — no third value exists.
        var values = Enum.GetValues<ComparacaoCmDecisionKind>();
        Assert.Equal(
            [ComparacaoCmDecisionKind.Manter, ComparacaoCmDecisionKind.ColocarDeParte],
            values);

        Assert.Equal(2, PerCmDecisionVocabulary.All.Count);
        Assert.Equal(
            ["Manter", "Colocar de parte"],
            PerCmDecisionVocabulary.All);
    }

    [Fact]
    public void Tokens_RoundTripExplicitly()
    {
        Assert.Equal("manter", ComparacaoCmDecisionKindTokens.ToToken(ComparacaoCmDecisionKind.Manter));
        Assert.Equal("colocar_de_parte", ComparacaoCmDecisionKindTokens.ToToken(ComparacaoCmDecisionKind.ColocarDeParte));

        Assert.Equal(
            ComparacaoCmDecisionKind.Manter,
            ComparacaoCmDecisionKindTokens.Parse("manter"));
        Assert.Equal(
            ComparacaoCmDecisionKind.ColocarDeParte,
            ComparacaoCmDecisionKindTokens.Parse("colocar_de_parte"));

        // Unknown or blank tokens parse to null — no case folding, no fuzzy matching.
        Assert.Null(ComparacaoCmDecisionKindTokens.Parse("Manter"));
        Assert.Null(ComparacaoCmDecisionKindTokens.Parse("colocar-de-parte"));
        Assert.Null(ComparacaoCmDecisionKindTokens.Parse(null));
        Assert.Null(ComparacaoCmDecisionKindTokens.Parse(""));
    }

    [Fact]
    public void TheTokensMatchTheDatabaseCheckVocabulary()
    {
        // The two stored tokens are exactly the values of the subject decision CHECK.
        Assert.Equal(
            ComparacaoCmDecisionKindTokens.ToToken(ComparacaoCmDecisionKind.Manter) + "," +
            ComparacaoCmDecisionKindTokens.ToToken(ComparacaoCmDecisionKind.ColocarDeParte),
            "manter,colocar_de_parte");
    }
}