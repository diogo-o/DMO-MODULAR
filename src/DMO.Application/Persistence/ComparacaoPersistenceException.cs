namespace DMO.Application.Persistence;

/// <summary>The typed persistence failure reasons of the Peso Comparação writes (the binding
/// rules of the comparison repository).</summary>
public enum ComparacaoPersistenceFailureReason
{
    /// <summary>A RESTRICT foreign key refused the write (a referenced Peso / CM context / user
    /// vanished, or a duplicate identity was attempted).</summary>
    DependencyExists,

    /// <summary>The put-aside justification CHECK backstop refused the write.</summary>
    ReasonRequired,

    /// <summary>The decision-state consistency CHECK backstop refused the write (defensive — the
    /// service guards the state; the decision is final per compared CM).</summary>
    InvalidDecisionState,

    /// <summary>The natural key (comparacao_id, cm_id) was duplicated (defensive — the service
    /// guards selection).</summary>
    DuplicateCmSelected,

    /// <summary>A computed comparison row result is not strictly positive (the C2 backstop).</summary>
    ResultNonPositive,
}

/// <summary>
/// The typed persistence failure of the Peso Comparação writes.
/// </summary>
/// <remarks>
/// Mirrors the accepted <c>ControloPersistenceException</c> convention: PostgreSQL constraint
/// violations are mapped onto typed application failures so a validator-passing combination can
/// never surface as a 500.
/// </remarks>
public sealed class ComparacaoPersistenceException : Exception
{
    /// <summary>Creates the typed failure.</summary>
    public ComparacaoPersistenceException(ComparacaoPersistenceFailureReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    /// <summary>The typed failure reason.</summary>
    public ComparacaoPersistenceFailureReason Reason { get; }
}