using DMO.Application.Controlo.Approve;
using DMO.Domain.Controlo;

namespace DMO.UnitTests.Controlo.Approve;

/// <summary>
/// P2-T06 unit tests — the closed command shapes (D1), the validator rules (J2/O3/Q-NOTE, FILTER_INVALID,
/// review-state and decision filter vocabularies) and the list-query validation (AC-AP3/H2).
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract §9.2/§12.2/§15.1/§15.2 and §26.4 rows D1/J2/O3.
/// </remarks>
public sealed class ControloApproveValidatorTests
{
    private static readonly Guid PesoId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ---- D1 (AC-D1/AC-D2): the command carriers contain ONLY identity/version/reason ---------

    [Fact]
    public void D1_ApproveCommandCarriesOnlyIdentityAndVersion()
    {
        var properties = typeof(ApprovePesoCommand).GetProperties();
        Assert.Equal(2, properties.Length);
        Assert.Contains(properties, property => property.Name == nameof(ApprovePesoCommand.PesoId));
        Assert.Contains(properties, property => property.Name == nameof(ApprovePesoCommand.ExpectedVersion));
    }

    [Fact]
    public void D1_RejectAndReopenCommandsCarryOnlyIdentityVersionAndReason()
    {
        foreach (var type in new[] { typeof(RejectPesoCommand), typeof(ReopenPesoCommand) })
        {
            var properties = type.GetProperties();
            Assert.Equal(3, properties.Length);
            Assert.Contains(properties, property => property.Name == "PesoId");
            Assert.Contains(properties, property => property.Name == "ExpectedVersion");
            Assert.Contains(properties, property => property.Name == "Reason");
        }
    }

    [Fact]
    public void D1_ApprovalOnlyPresentationComposesAroundTheSharedReadModel()
    {
        // RD3: the wrapper adds only approval-only facts; nothing attempts to re-define the Peso.
        var properties = typeof(ReviewSheetReadModel).GetProperties();
        Assert.Equal(3, properties.Length);
        Assert.Contains(properties, property => property.Name == "Peso");
        Assert.Contains(properties, property => property.Name == "Decisions");
        Assert.Contains(properties, property => property.Name == "Availability");

        // RD1: the review sheet embeds the EXACT shared P2-T05 type (type identity assertion).
        var pesoProperty = typeof(ReviewSheetReadModel).GetProperty("Peso")!;
        Assert.Equal(typeof(DMO.Application.Controlo.Pesos.PesoSheetReadModel), pesoProperty.PropertyType);
    }

    // ---- J2/O3 (AC-J2/AC-O3): reason requirements -------------------------------------------

    [Fact]
    public void J2_RejectWithBlankOrMissingReasonIsRefusedBeforeAnyWrite()
    {
        Assert.Equal(
            [ControloApproveValidationErrors.RejectReasonRequired],
            ControloApproveValidator.ValidateReject(new RejectPesoCommand(PesoId, 1, string.Empty)));
        Assert.Equal(
            [ControloApproveValidationErrors.RejectReasonRequired],
            ControloApproveValidator.ValidateReject(new RejectPesoCommand(PesoId, 1, "   ")));
        Assert.Equal(
            [ControloApproveValidationErrors.RejectReasonRequired],
            ControloApproveValidator.ValidateReject(new RejectPesoCommand(PesoId, 1, null!)));
    }

    [Fact]
    public void O3_ReopenWithBlankOrMissingReasonIsRefusedBeforeAnyWrite()
    {
        Assert.Equal(
            [ControloApproveValidationErrors.ReopenReasonRequired],
            ControloApproveValidator.ValidateReopen(new ReopenPesoCommand(PesoId, 1, string.Empty)));
        Assert.Equal(
            [ControloApproveValidationErrors.ReopenReasonRequired],
            ControloApproveValidator.ValidateReopen(new ReopenPesoCommand(PesoId, 1, "  \t ")));
    }

    [Fact]
    public void J2_RejectWithANonBlankReasonPassesValidation()
    {
        Assert.Empty(ControloApproveValidator.ValidateReject(new RejectPesoCommand(PesoId, 1, "Peso com leitura fora do esperado.")));
        Assert.Empty(ControloApproveValidator.ValidateReopen(new ReopenPesoCommand(PesoId, 1, "Correção da temperatura submetida.")));
    }

    // ---- FILTER_INVALID (AC-AP3/H2): paging and filter vocabulary ---------------------------

    [Fact]
    public void FilterInvalid_PagingBoundsAreRejectedNeverSilentlyClipped()
    {
        Assert.Contains(
            ControloApproveValidationErrors.FilterInvalid,
            ControloApproveValidator.Validate(new PendingListQuery(null, null, null, null, null, null, null, Page: 0, PageSize: 50)));
        Assert.Contains(
            ControloApproveValidationErrors.FilterInvalid,
            ControloApproveValidator.Validate(new PendingListQuery(null, null, null, null, null, null, null, Page: 1, PageSize: 0)));
        Assert.Contains(
            ControloApproveValidationErrors.FilterInvalid,
            ControloApproveValidator.Validate(new PendingListQuery(null, null, null, null, null, null, null, Page: 1, PageSize: 101)));

        Assert.Empty(ControloApproveValidator.Validate(new PendingListQuery(null, null, null, null, null, null, null, Page: 1, PageSize: 100)));
    }

    [Fact]
    public void FilterInvalid_BlankTraversalFiltersAreRejected()
    {
        Assert.Contains(
            ControloApproveValidationErrors.FilterInvalid,
            ControloApproveValidator.Validate(new PendingListQuery("  ", null, null, null, null, null, null)));
    }

    [Fact]
    public void FilterInvalid_UnknownReviewStateAndDecisionTokensAreRejected()
    {
        var unknownState = new HistoryListQuery(ReviewState: "bloqueado", null, null, null, null, null, null, null, null, null, null, null);
        Assert.Contains(ControloApproveValidationErrors.FilterInvalid, ControloApproveValidator.Validate(unknownState));

        var unknownDecision = new HistoryListQuery(null, null, null, null, null, null, null, null, null, null, null, Decision: "aprovado-2");
        Assert.Contains(ControloApproveValidationErrors.FilterInvalid, ControloApproveValidator.Validate(unknownDecision));
    }

    [Fact]
    public void FilterInvalid_ClosedVocabulariesAreAccepted()
    {
        foreach (var state in new[] { "submetido", "pendente", "aprovado", "nao_aprovado" })
        {
            Assert.Empty(ControloApproveValidator.Validate(
                new HistoryListQuery(ReviewState: state, null, null, null, null, null, null, null, null, null, null, null)));
        }

        foreach (var decision in new[] { "aprovado", "nao_aprovado", "reaberto" })
        {
            Assert.Empty(ControloApproveValidator.Validate(
                new HistoryListQuery(null, null, null, null, null, null, null, null, null, null, null, Decision: decision)));
        }
    }
}