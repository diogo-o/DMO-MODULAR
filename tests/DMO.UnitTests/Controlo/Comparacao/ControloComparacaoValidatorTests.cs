using DMO.Application.Controlo.Comparacao;
using DMO.Domain.ControloComparacao;

namespace DMO.UnitTests.Controlo.Comparacao;

/// <summary>
/// The pure Comparação validator: the exact closed codes of the Peso Comparação area.
/// </summary>
public sealed class ControloComparacaoValidatorTests
{
    [Fact]
    public void Start_RequiresThePesoId()
    {
        Assert.Equal(
            [ComparacaoValidationErrors.PesoIdRequired],
            ControloComparacaoValidator.Validate(new StartComparacaoCommand(Guid.Empty, Guid.NewGuid())));

        Assert.Empty(ControloComparacaoValidator.Validate(
            new StartComparacaoCommand(Guid.NewGuid(), Guid.NewGuid())));
    }

    [Fact]
    public void AddSubject_RequiresTheComparacaoIdAndACanonicalCmId()
    {
        // The validator emits in its fixed order: the comparison identity, then the CM identity.
        Assert.Equal(
            new[] { ComparacaoValidationErrors.ComparacaoIdRequired, ComparacaoValidationErrors.CmContextNotFound },
            ControloComparacaoValidator.Validate(
                new AddComparacaoCmSubjectCommand(Guid.Empty, Guid.Empty, Guid.NewGuid())));

        Assert.Equal(
            [ComparacaoValidationErrors.ComparacaoIdRequired],
            ControloComparacaoValidator.Validate(
                new AddComparacaoCmSubjectCommand(Guid.Empty, Guid.NewGuid(), Guid.NewGuid())));

        Assert.Equal(
            [ComparacaoValidationErrors.CmContextNotFound],
            ControloComparacaoValidator.Validate(
                new AddComparacaoCmSubjectCommand(Guid.NewGuid(), Guid.Empty, Guid.NewGuid())));

        Assert.Empty(ControloComparacaoValidator.Validate(
            new AddComparacaoCmSubjectCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())));
    }

    [Fact]
    public void RecordMeasurements_ReusesTheSharedPesoRowRules()
    {
        var subject = (ComparacaoId: Guid.NewGuid(), CmId: Guid.NewGuid());

        // Missing rows / invalid weights produce the SHARED P2-T05 machine tokens.
        var noRows = ControloComparacaoValidator.Validate(
            new RecordComparacaoMeasurementsCommand(subject.ComparacaoId, subject.CmId, 1, []));
        Assert.Contains(ComparacaoValidationErrors.RowRequired, noRows);

        var invalidWeight = ControloComparacaoValidator.Validate(
            new RecordComparacaoMeasurementsCommand(subject.ComparacaoId, subject.CmId, 1, [0m, -1m]));
        Assert.Contains(ComparacaoValidationErrors.RowWeightInvalid, invalidWeight);

        // Empty natural-key parts.
        var emptyIds = ControloComparacaoValidator.Validate(
            new RecordComparacaoMeasurementsCommand(Guid.Empty, Guid.Empty, 1, [100m]));
        Assert.Contains(ComparacaoValidationErrors.ComparacaoIdRequired, emptyIds);
        Assert.Contains(ComparacaoValidationErrors.CmContextNotFound, emptyIds);

        // The dense positions validate cleanly (no position tokens for a valid list).
        var valid = ControloComparacaoValidator.Validate(
            new RecordComparacaoMeasurementsCommand(subject.ComparacaoId, subject.CmId, 1, [100m, 200m]));
        Assert.DoesNotContain(ComparacaoValidationErrors.RowPositionInvalid, valid);
        Assert.DoesNotContain(ComparacaoValidationErrors.DuplicateRowPosition, valid);
        Assert.Empty(valid);
    }

    [Fact]
    public void Decide_EnforcesTheClosedVocabularyAndThePutAsideJustification()
    {
        var subject = (ComparacaoId: Guid.NewGuid(), CmId: Guid.NewGuid());

        // Unknown decision token.
        var unknown = ControloComparacaoValidator.Validate(
            new DecideComparacaoCmCommand(subject.ComparacaoId, subject.CmId, 1, "inventado", null, Guid.NewGuid()));
        Assert.Equal([ComparacaoValidationErrors.DecisionUnknown], unknown);

        // Put aside without justification is refused; with justification it passes.
        var noReason = ControloComparacaoValidator.Validate(
            new DecideComparacaoCmCommand(subject.ComparacaoId, subject.CmId, 1, "colocar_de_parte", "  ", Guid.NewGuid()));
        Assert.Equal([ComparacaoValidationErrors.PutAsideReasonRequired], noReason);

        var withReason = ControloComparacaoValidator.Validate(
            new DecideComparacaoCmCommand(subject.ComparacaoId, subject.CmId, 1, "colocar_de_parte", "Fuga detetada", Guid.NewGuid()));
        Assert.Empty(withReason);

        // Manter requires NO fabricated reason.
        var manter = ControloComparacaoValidator.Validate(
            new DecideComparacaoCmCommand(subject.ComparacaoId, subject.CmId, 1, "manter", null, Guid.NewGuid()));
        Assert.Empty(manter);

        // Empty natural-key parts.
        var emptyIds = ControloComparacaoValidator.Validate(
            new DecideComparacaoCmCommand(Guid.Empty, Guid.Empty, 1, "manter", null, Guid.NewGuid()));
        Assert.Contains(ComparacaoValidationErrors.ComparacaoIdRequired, emptyIds);
        Assert.Contains(ComparacaoValidationErrors.CmContextNotFound, emptyIds);
    }

    [Fact]
    public void Confirm_RequiresTheComparacaoId()
    {
        Assert.Equal(
            [ComparacaoValidationErrors.ComparacaoIdRequired],
            ControloComparacaoValidator.Validate(new ConfirmComparacaoCommand(Guid.Empty, 1, Guid.NewGuid())));

        Assert.Empty(ControloComparacaoValidator.Validate(
            new ConfirmComparacaoCommand(Guid.NewGuid(), 1, Guid.NewGuid())));
    }
}