namespace DMO.Application.JobOn;

/// <summary>
/// The Job On control-outputs read contract: the read-only projection that exposes the existing
/// Controlo outputs of ONE occurrence on the Job On surface (this slice: the Peso PDF only).
/// </summary>
/// <remarks>
/// <para>
/// The service composes three owners and duplicates none of them: the occurrence comes from the
/// Job On repository (<c>jobon_id</c>), the related Pesos come from the Controlo-owned read seam
/// (<c>jobon_id → peso_id</c> through the real <c>cm_contexts</c> anchors), and the file
/// resolution/existence/read stays encapsulated in the Documents area (<c>IPesoPdfDocumentRead</c>
/// — the configured base directory and the deterministic naming never leave that area). The Job On
/// surface never touches the filesystem and never holds an absolute path.</para>
/// <para>
/// Read-only everywhere: no write, no version bump and no creation (P2-T04 read-transaction rule);
/// the projection is composed at read time and is never persisted — no metadata is duplicated on
/// the Job On side.</para>
/// </remarks>
public interface IJobOnControlOutputsService
{
    /// <summary>
    /// Reads the outputs projection of ONE occurrence (related Pesos + their document
    /// availability), in the stable order of the Controlo read, or <c>NotFound</c>.
    /// </summary>
    Task<JobOnControlOutputsResult> GetAsync(
        Guid jobOnId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the stored Peso PDF bytes of ONE related Peso of the occurrence — the controlled open
    /// read: the related-peso scope is enforced, so a Peso of another production is never served.
    /// </summary>
    Task<JobOnControlOutputContentResult> ReadAsync(
        Guid jobOnId,
        Guid pesoId,
        CancellationToken cancellationToken);
}