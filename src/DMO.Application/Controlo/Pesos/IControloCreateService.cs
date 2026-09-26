namespace DMO.Application.Controlo.Pesos;

/// <summary>
/// The single Peso create/measurement/submission service contract.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §20.3.
/// <para>
/// The service composes the P2-T04 application contracts (<c>IJobOnService</c>, <c>IToolService</c>)
/// for selection/context reads and for the missing-CM association (§4.3): the Web layer never
/// queries the database directly and no P2-T05 repository reads another module's tables. The
/// service holds no <c>HttpContext</c>, no route knowledge and no presentation type.
/// </para>
/// <para>
/// <see cref="CalculateAsync"/> is the stateless Route 8 calculation: request-carrier identity
/// only — no path identity, no record resolution, no write, no version bump, no id allocation
/// (C3/AC-R7).</para>
/// </remarks>
public interface IControloCreateService
{
    /// <summary>Stateless per-row calculation over exactly one anchor (read-only).</summary>
    Task<PesoResult> CalculateAsync(CalculatePesoCommand command, CancellationToken cancellationToken);

    /// <summary>Creates the Peso draft atomically (record + full row set; backend-allocated ids).</summary>
    Task<PesoResult> CreateAsync(CreatePesoCommand command, CancellationToken cancellationToken);

    /// <summary>Reads the published Peso sheet (read-only projection), or <c>NotFound</c>.</summary>
    Task<PesoResult> GetAsync(Guid pesoId, CancellationToken cancellationToken);

    /// <summary>Edits the draft (same <c>peso_id</c>, whole row set replaced, recomputed results).</summary>
    Task<PesoResult> UpdateAsync(UpdatePesoCommand command, CancellationToken cancellationToken);

    /// <summary>Submits the same <c>peso_id</c> into the reviewable handoff with backend attribution.</summary>
    Task<PesoResult> SubmitAsync(SubmitPesoCommand command, CancellationToken cancellationToken);

    /// <summary>Explicitly associates a pending Peso with a candidate <c>cm_id</c> (anchor swap).</summary>
    Task<PesoResult> AssociateAsync(AssociatePesoCommand command, CancellationToken cancellationToken);

    /// <summary>Lists the real association candidates resolving to the anchor Tool (route 4).</summary>
    Task<PesoResult> ListAssociationCandidatesAsync(Guid toolId, CancellationToken cancellationToken);
}