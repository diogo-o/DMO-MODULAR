namespace DMO.Domain.ControloComparacao;

/// <summary>
/// One comparison measurement row of one compared CM subject: the re-entered weight plus the
/// backend-derived results of the re-measurement.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="WaterWeightG"/> is the operator re-entry of the "Peso de água" (g). <see cref="CapacityCm3"/>
/// and <see cref="GlassWeightG"/> are computed by the backend with the SAME Peso calculation
/// rules as the initial Peso (the shared Peso row calculation path — never a duplicated formula)
/// and stored with the row as registered per-row results. The rows live under the compared CM
/// subject (the natural identity <see cref="ComparacaoId"/> + <see cref="CmId"/>), physically
/// separated from <c>peso_measurement_rows</c>: comparison measurements are distinct NEW results
/// that never contribute to or modify the initial Peso average.</para>
/// <para>
/// The row identity is the natural <c>(comparacao_id, cm_id, row_position)</c> triple — no
/// reading UUID exists (no workflow independently addresses one comparison reading). The row
/// carries no <c>version</c>: it is only written inside a subject mutation transaction and the
/// subject's version protects the aggregate (the accepted <c>PesoMeasurementRow</c> convention).
/// The dense 1-based <see cref="RowPosition"/> is the positional pairing key within the subject.</para>
/// </remarks>
public sealed record ComparacaoMeasurementRow(
    ComparacaoId ComparacaoId,
    Guid CmId,
    int RowPosition,
    decimal WaterWeightG,
    decimal CapacityCm3,
    decimal GlassWeightG,
    DateTimeOffset CreatedAt);