using DMO.Application.Documents;

namespace DMO.UnitTests.Documents;

/// <summary>
/// Unit proofs of the closed Peso document naming convention
/// (<c>&lt;reference&gt;/&lt;production-number&gt;/Peso_&lt;reference&gt;_&lt;machine&gt;.pdf</c>,
/// P2-T05 contract §12.1 / delta §7.4): the exact target shape and the fail-closed segment rules
/// (no traversal, no separator, no reserved character, no silent rewriting).
/// </summary>
public sealed class PesoPdfNamingTests
{
    [Fact]
    public void Convention_ComposesTheExactDeterministicTarget()
    {
        var composed = PesoPdfNaming.TryCompose("REF-X", "2026-001", "B1", out var path);

        Assert.True(composed);
        Assert.NotNull(path);
        Assert.Equal("REF-X/2026-001", path!.RelativeDirectory);
        Assert.Equal("Peso_REF-X_B1.pdf", path.FileName);
        Assert.Equal("REF-X/2026-001/Peso_REF-X_B1.pdf", path.RelativePath);
    }

    [Fact]
    public void Convention_UsesTheSuppliedFactsVerbatim_NeverNormalized()
    {
        var composed = PesoPdfNaming.TryCompose("ART-100", "P-0042", "C2", out var path);

        Assert.True(composed);
        Assert.Equal("Peso_ART-100_C2.pdf", path!.FileName);
        Assert.Equal("ART-100/P-0042", path.RelativeDirectory);
        Assert.Equal("ART-100/P-0042/Peso_ART-100_C2.pdf", path.RelativePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Convention_FailsClosedOnBlankSegments(string? reference)
    {
        Assert.False(PesoPdfNaming.TryCompose(reference, "2026-001", "B1", out var path));
        Assert.Null(path);
    }

    [Theory]
    [InlineData("..", "2026-001", "B1")]
    [InlineData(".", "2026-001", "B1")]
    [InlineData("a/b", "2026-001", "B1")]
    [InlineData("a\\b", "2026-001", "B1")]
    [InlineData("REF:1", "2026-001", "B1")]
    [InlineData("REF?1", "2026-001", "B1")]
    [InlineData("REF*1", "2026-001", "B1")]
    [InlineData(" REF", "2026-001", "B1")]
    [InlineData("REF ", "2026-001", "B1")]
    [InlineData("REF-AAA", "..", "B1")]
    [InlineData("REF-AAA", "2026\\001", "B1")]
    [InlineData("REF-AAA", "2026-001", "B1/2")]
    public void Convention_FailsClosedOnUnsafeSegments(
        string reference,
        string productionNumber,
        string machine)
    {
        Assert.False(PesoPdfNaming.TryCompose(reference, productionNumber, machine, out var path));
        Assert.Null(path);
    }

    [Fact]
    public void Convention_FailsClosedOnOverLongSegments()
    {
        var longReference = new string('R', PesoPdfNaming.MaxSegmentLength + 1);

        Assert.False(PesoPdfNaming.TryCompose(longReference, "2026-001", "B1", out var path));
        Assert.Null(path);
    }

    [Fact]
    public void Convention_AcceptsTheMaximumLengthSegment()
    {
        var longReference = new string('R', PesoPdfNaming.MaxSegmentLength);

        Assert.True(PesoPdfNaming.TryCompose(longReference, "2026-001", "B1", out var path));
        Assert.NotNull(path);
    }
}