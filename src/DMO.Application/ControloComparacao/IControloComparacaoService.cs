namespace DMO.Application.ControloComparacao;

/// <summary>
/// The Peso Comparação application service contract: the optional comparison child of the
/// initial Peso — start, select compared CMs, record NEW comparison measurements with the shared
/// Peso calculation path, decide each measured CM individually, confirm, and read.
/// </summary>
/// <remarks>
/// Authority: the Peso Comparação functional authority. Comparação belongs to Peso and happens
/// during production; it is optional (a Peso may have none and then behaves exactly as today)
/// and may contain only the CMs that need extra checking (1, 2, 4, … — never a fixed number).
/// Every compared CM is identified by the natural <c>comparacao_id + cm_id</c> — the exact
/// frozen CM identity of the production context. Comparison measurements are NEW results stored
/// separately and never contribute to or modify the initial Peso average. No automatic business
/// decision exists: every measured CM requires its own explicit human decision, put-aside
/// requires justification, and no Tool/warehouse/JobOn configuration is ever changed
/// automatically.
/// </remarks>
public interface IControloComparacaoService
{
    /// <summary>Starts the optional comparison of one Peso (creates the header only; the initial
    /// Peso values/status/approval are never changed).</summary>
    Task<ComparacaoResult> StartAsync(StartComparacaoCommand command, CancellationToken cancellationToken);

    /// <summary>Selects one compared CM subject (the exact canonical <c>cm_id</c>).</summary>
    Task<ComparacaoResult> AddSubjectAsync(AddComparacaoCmSubjectCommand command, CancellationToken cancellationToken);

    /// <summary>Records the NEW comparison measurements of one subject (same calculation rules as
    /// the initial Peso, over its frozen facts; whole-set replacement).</summary>
    Task<ComparacaoResult> RecordMeasurementsAsync(
        RecordComparacaoMeasurementsCommand command,
        CancellationToken cancellationToken);

    /// <summary>Applies the INDIVIDUAL explicit human decision of one compared CM.</summary>
    Task<ComparacaoResult> DecideAsync(DecideComparacaoCmCommand command, CancellationToken cancellationToken);

    /// <summary>Explicitly confirms the comparison (refused while a measured CM lacks its decision).</summary>
    Task<ComparacaoResult> ConfirmAsync(ConfirmComparacaoCommand command, CancellationToken cancellationToken);

    /// <summary>Reads one comparison by its id.</summary>
    Task<ComparacaoResult> GetAsync(Guid comparacaoId, CancellationToken cancellationToken);

    /// <summary>Reads the comparison of one Peso, or the explicit <c>NoComparison</c> absence.</summary>
    Task<ComparacaoResult> GetByPesoIdAsync(Guid pesoId, CancellationToken cancellationToken);
}