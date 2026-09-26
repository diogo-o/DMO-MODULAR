namespace DMO.Application.Controlo.Pesos;

/// <summary>
/// The single shared per-row Peso calculation path: capacity and glass-weight derived from the
/// entered water weight under the authoritative §5.3 formulas.
/// </summary>
/// <remarks>
/// <para>
/// The formulas: <c>Capacidade = Peso de água ÷ densidade da água para a temperatura introduzida</c>;
/// <c>Peso do vidro = (Capacidade + Volume Marisa/BQ − Volume Punção/PU) × Densidade do vidro</c>,
/// each result rounded to <c>numeric(18,4)</c> with <see cref="MidpointRounding.AwayFromZero"/>.
/// This helper is the ONE implementation of the per-row computation: the Peso create service
/// (<c>ControloCreateService.ComputeRows</c>) delegates here and the Peso Comparação
/// re-measurement path calls here directly — no formula is ever duplicated (the shared path is
/// the calculation-reuse contract of the Comparação slice).</para>
/// <para>
/// A computed per-row result that is not strictly positive is refused BEFORE any write with the
/// typed <c>RESULT_NON_POSITIVE</c> token (C2) — never a 500. An invalid water-density/glass-density
/// configuration (a non-positive divisor or density) can never fabricate a result: the same
/// refusal is produced.</para>
/// </remarks>
internal static class PesoRowCalculationRules
{
    /// <summary>
    /// Computes every per-row result with the authoritative §5.3 formulas over the supplied
    /// row water weights and the two density facts. Returns the computed rows, or <c>null</c>
    /// with the closed error tokens when any per-row result is not strictly positive.
    /// </summary>
    public static IReadOnlyList<PesoRowCalculation>? ComputeRows(
        IReadOnlyList<decimal> waterWeightsG,
        decimal waterDensity,
        decimal glassDensity,
        decimal? volumeMarisaBq,
        decimal? volumePuncaoPu,
        out IReadOnlyList<string> errors)
    {
        errors = [];

        // An invalid water-density/glass-density configuration can never fabricate a result
        // (MES11): a non-positive density means the entered combination yields no strictly
        // positive derived result — the typed RESULT_NON_POSITIVE refusal, nothing written.
        if (waterDensity <= 0 || glassDensity <= 0)
        {
            errors = [ControloCreateValidationErrors.ResultNonPositive];
            return null;
        }

        var rows = new List<PesoRowCalculation>(waterWeightsG.Count);

        for (var index = 0; index < waterWeightsG.Count; index++)
        {
            var capacity = decimal.Round(waterWeightsG[index] / waterDensity, 4, MidpointRounding.AwayFromZero);
            var glass = decimal.Round(
                (capacity + (volumeMarisaBq ?? 0) - (volumePuncaoPu ?? 0)) * glassDensity,
                4,
                MidpointRounding.AwayFromZero);

            if (capacity <= 0 || glass <= 0)
            {
                errors = [ControloCreateValidationErrors.ResultNonPositive];
                return null;
            }

            rows.Add(new PesoRowCalculation(index + 1, waterWeightsG[index], capacity, glass));
        }

        return rows;
    }
}