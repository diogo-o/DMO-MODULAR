using DMO.Application.Controlo.Approve;

namespace DMO.UnitTests.Controlo.Approve;

/// <summary>
/// P2-T06 unit tests — contract §26.4 row CP4 (AC-CP1/AC-CP2/AC-CP4): the exact-relation
/// composition contract is pinned over the composition function with a SUPPLIED carrier — the
/// exact supplied <c>previous_peso_id</c> is retained, current and previous context are
/// retained exactly, no substitution/fallback/mutation occurs and the result derives only from
/// the supplied data; without a carrier nothing is composed (no synthetic Comparação).
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract §17.2 (carrier truth — exact relation, no heuristic), §26.4 row
/// CP4 ("unit test over the composition function with a supplied carrier") and §30 AC-CP1 /
/// AC-CP2 / AC-CP4. The seam is dormant in production (no carrier exists; no call site); this
/// test pins the FUTURE composition contract.
/// </remarks>
public sealed class ComparisonCompositionTests
{
    private static readonly Guid CurrentPesoId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly Guid PreviousPesoId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    /// <summary>
    /// A supplied carrier whose current side carries facts a heuristic would find "tempting"
    /// (a recent submission, a current reference) — proving the composition never falls back to
    /// them or substitutes them for the supplied relation.
    /// </summary>
    private static ComparisonCarrier CarrierWithTemptingAlternatives() => new(
        Current: new ComparisonPesoContext(
            CurrentPesoId,
            Reference: "REF-2026-0007",
            ProductionNumber: "P-7007",
            SubmittedAt: DateTimeOffset.UtcNow.AddHours(-3)),
        Previous: new ComparisonPesoContext(
            PreviousPesoId,
            Reference: "REF-2026-0001",
            ProductionNumber: "P-7001",
            SubmittedAt: DateTimeOffset.UtcNow.AddDays(-2)));

    [Fact]
    public void CP4_WithASuppliedCarrier_TheExactSuppliedPreviousPesoIdIsRetained()
    {
        var carrier = CarrierWithTemptingAlternatives();

        var composition = Assert.IsType<ComparisonComposition>(ComparisonComposer.Compose(carrier));

        // The composition exposes exactly the supplied previous_peso_id (AC-CP1): the previous
        // side's id, with nothing substituted and nothing invented.
        Assert.Equal(PreviousPesoId, composition.PreviousPesoId);
        Assert.Equal(PreviousPesoId, composition.Previous.PesoId);
        Assert.Equal(CurrentPesoId, composition.CurrentPesoId);
        Assert.Equal(CurrentPesoId, composition.Current.PesoId);
    }

    [Fact]
    public void CP4_WithASuppliedCarrier_CurrentAndPreviousContextAreRetainedExactly()
    {
        var carrier = CarrierWithTemptingAlternatives();

        var composition = Assert.IsType<ComparisonComposition>(ComparisonComposer.Compose(carrier));

        // Same instances — the composition is a passthrough: nothing is copied, rebuilt,
        // completed or inferred (AC-CP2: context is the supplied context, never inference).
        Assert.Same(carrier.Current, composition.Current);
        Assert.Same(carrier.Previous, composition.Previous);
        Assert.Equal(carrier.Current, composition.Current);
        Assert.Equal(carrier.Previous, composition.Previous);

        // Every supplied context fact is preserved verbatim on both sides.
        Assert.Equal("REF-2026-0007", composition.Current.Reference);
        Assert.Equal("P-7007", composition.Current.ProductionNumber);
        Assert.Equal(carrier.Current.SubmittedAt, composition.Current.SubmittedAt);
        Assert.Equal("REF-2026-0001", composition.Previous.Reference);
        Assert.Equal("P-7001", composition.Previous.ProductionNumber);
        Assert.Equal(carrier.Previous.SubmittedAt, composition.Previous.SubmittedAt);

        // A carrier that supplies NULL context facts keeps them NULL — no fabricated context
        // values are ever exposed (AC-CP4: nothing invented).
        var sparse = new ComparisonCarrier(
            Current: new ComparisonPesoContext(CurrentPesoId, Reference: null, ProductionNumber: null, SubmittedAt: null),
            Previous: new ComparisonPesoContext(PreviousPesoId, Reference: null, ProductionNumber: null, SubmittedAt: null));

        var sparseComposition = Assert.IsType<ComparisonComposition>(ComparisonComposer.Compose(sparse));
        Assert.Null(sparseComposition.Current.Reference);
        Assert.Null(sparseComposition.Current.ProductionNumber);
        Assert.Null(sparseComposition.Current.SubmittedAt);
        Assert.Null(sparseComposition.Previous.Reference);
        Assert.Null(sparseComposition.Previous.ProductionNumber);
        Assert.Null(sparseComposition.Previous.SubmittedAt);
        Assert.Equal(PreviousPesoId, sparseComposition.PreviousPesoId);
    }

    [Fact]
    public void CP4_WithASuppliedCarrier_NoSubstitutionNoFallbackNoMutationAndNoHiddenInput()
    {
        var carrier = CarrierWithTemptingAlternatives();

        var result = Assert.IsType<ComparisonComposition>(ComparisonComposer.Compose(carrier));

        // No substitution: the exposed relation is exactly the supplied one (the "tempting
        // alternatives" — the current reference or the recent submission — are never used).
        Assert.Equal(PreviousPesoId, result.PreviousPesoId);
        Assert.Equal(CurrentPesoId, result.CurrentPesoId);

        // No mutation: the supplied carrier is unchanged after composition.
        Assert.Equal(PreviousPesoId, carrier.Previous.PesoId);
        Assert.Equal(CurrentPesoId, carrier.Current.PesoId);
        Assert.Equal("REF-2026-0007", carrier.Current.Reference);
        Assert.Equal("P-7007", carrier.Current.ProductionNumber);
        Assert.Equal("REF-2026-0001", carrier.Previous.Reference);
        Assert.Equal("P-7001", carrier.Previous.ProductionNumber);
        Assert.Same(carrier.Current, result.Current);
        Assert.Same(carrier.Previous, result.Previous);

        // The result derives only from the supplied data: composing the SAME carrier twice is
        // deterministic (no hidden state, no external lookup), and a DIFFERENT supplied carrier
        // yields a different composition — the output tracks the input exactly.
        var again = Assert.IsType<ComparisonComposition>(ComparisonComposer.Compose(carrier));
        Assert.Equal(result, again);

        var otherPrevious = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var otherCarrier = carrier with
        {
            Previous = carrier.Previous with { PesoId = otherPrevious },
        };

        var otherResult = Assert.IsType<ComparisonComposition>(ComparisonComposer.Compose(otherCarrier));
        Assert.Equal(otherPrevious, otherResult.PreviousPesoId);
        Assert.NotEqual(result.PreviousPesoId, otherResult.PreviousPesoId);
    }

    [Fact]
    public void CP4_WithoutACarrier_NothingIsComposed_NoSyntheticComparisonExists()
    {
        // Today no authoritative carrier exists (Comparação remainder NOT AUTHORIZED): the seam
        // composes NOTHING — no fabricated relation, no synthetic comparison state (AC-CP4).
        Assert.Null(ComparisonComposer.Compose(carrier: null));
    }
}