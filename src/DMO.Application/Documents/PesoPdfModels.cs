namespace DMO.Application.Documents;

/// <summary>
/// The composed Peso PDF document model — the ONLY facts the renderer may print.
/// </summary>
/// <remarks>
/// The model is composed from the SHARED P2-T05 <c>PesoSheetReadModel</c> (the same read used by
/// Create and Approve; never a parallel read and never a recomputation). The per-row results are
/// the FROZEN stored facts — the renderer never recalculates anything (no parallel calculation
/// path, P2-T08 rule). <see cref="Date"/> is the submission date (always present for the decided
/// records this document is generated for). The lot is the frozen CM lot rendered verbatim as a
/// plain value — no <c>L</c> prefix is ever added. The comparison vocabulary of the document is
/// exactly <b>Peso + Volume de água</b> per reading: the water MASS is a calculation input, never
/// a second comparison (the composer exposes the rows verbatim; the renderer labels the columns).
/// </remarks>
public sealed record PesoPdfDocumentModel(
    Guid PesoId,
    int Version,
    string EstadoLabel,
    string Reference,
    string ProductionNumber,
    string Machine,
    string Date,
    string CmReference,
    string? Processo,
    string Lot,
    decimal WaterTemperature,
    decimal? VolumeMarisaBq,
    decimal? VolumePuncaoPu,
    decimal? GlassDensityGCm3,
    string? PreviousProductionEndReference,
    string? PreviousAverageWeightReference,
    IReadOnlyList<PesoPdfRowModel> Rows)
{
    /// <summary>The average entered water mass of THIS sheet's rows (per-CM value, never global).</summary>
    public decimal? AverageWaterWeightG =>
        Rows.Count == 0 ? null : Rows.Average(row => row.WaterWeightG);

    /// <summary>The average water volume (capacity) of THIS sheet's rows (per-CM value, never global).</summary>
    public decimal? AverageCapacityCm3 =>
        Rows.Count == 0 ? null : Rows.Average(row => row.CapacityCm3);

    /// <summary>The average glass weight of THIS sheet's rows (per-CM value, never global).</summary>
    public decimal? AverageGlassWeightG =>
        Rows.Count == 0 ? null : Rows.Average(row => row.GlassWeightG);
}

/// <summary>One rendered Peso reading: the frozen per-row facts of the shared read model.</summary>
public sealed record PesoPdfRowModel(
    int Position,
    decimal WaterWeightG,
    decimal CapacityCm3,
    decimal GlassWeightG);

/// <summary>
/// The deterministic document target of one Peso (P2-T05 contract §12.1 / delta §7.4 convention):
/// <c>&lt;reference&gt;/&lt;production-number&gt;/Peso_&lt;reference&gt;_&lt;machine&gt;.pdf</c>.
/// </summary>
/// <remarks>
/// The filename/path is a deterministic OUTPUT convention — never an identity, never a join key
/// and never a second authority for reference/production/machine (those facts still come from the
/// Job On traversal facts of the shared read model; nothing is duplicated as authority).
/// </remarks>
public sealed record PesoPdfPath(
    string RelativeDirectory,
    string FileName,
    string RelativePath);

/// <summary>The outcome of one Peso PDF generation, shown to the operator.</summary>
public sealed record PesoPdfOutput(
    Guid PesoId,
    int Version,
    string FileName,
    string RelativePath,
    long Bytes);

/// <summary>The typed result of a Peso PDF generation request.</summary>
/// <remarks>
/// <see cref="Generated"/> is a newly written file; <see cref="AlreadyAvailable"/> is a file that
/// already exists at the deterministic target — the existing file is NEVER overwritten (frozen
/// outputs are not silently regenerated). Refusals are typed so an inaccessible workspace is
/// never conflated with a missing configuration or with a not-decided record.</remarks>
public abstract record PesoPdfResult
{
    /// <summary>The PDF was generated and stored at the deterministic target.</summary>
    public sealed record Generated(PesoPdfOutput Output) : PesoPdfResult;

    /// <summary>The deterministic target already holds a PDF of this Peso; nothing was rewritten.</summary>
    public sealed record AlreadyAvailable(PesoPdfOutput Output) : PesoPdfResult;

    /// <summary>The Peso does not exist.</summary>
    public sealed record NotFound(Guid PesoId) : PesoPdfResult;

    /// <summary>The request itself is invalid (defensive; the endpoint binds a plain id).</summary>
    public sealed record ValidationFailed(IReadOnlyList<string> Errors) : PesoPdfResult;

    /// <summary>The generation was refused with a typed reason (never a generic error).</summary>
    public sealed record Refused(PesoPdfRefusalReason Reason, string Message) : PesoPdfResult;
}

/// <summary>The typed refusal vocabulary of Peso PDF generation.</summary>
public enum PesoPdfRefusalReason
{
    /// <summary>No base directory is configured in Controlo_Create → Definições.</summary>
    PdfDirectoryNotConfigured,

    /// <summary>The Peso is not decided yet (docs become available after the decision).</summary>
    NotDecided,

    /// <summary>The Peso has no production binding (Job On por associar) — no document target exists.</summary>
    ProductionBindingMissing,

    /// <summary>The configured workspace is not usable from the server process (missing/not a
    /// directory/access denied/failed check) — distinguishable from a missing file.</summary>
    WorkspaceUnavailable,

    /// <summary>A reference/production/machine value cannot form a safe document target.</summary>
    InvalidFileName,

    /// <summary>The file could not be written for another infrastructure reason.</summary>
    WriteFailed,
}