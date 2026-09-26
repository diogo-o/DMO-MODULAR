using DMO.Application.Controlo.Pesos;
using DMO.Application.Documents;
using DMO.Application.Repositories;
using DMO.Domain.Tools;
using DomainJobOn = DMO.Domain.JobOn.JobOn;

namespace DMO.Application.JobOn;

/// <summary>
/// The Job On control-outputs composition: occurrence + related Pesos + Peso PDF availability/
/// content — the read projection of this slice (<c>jobon_id → peso_id → Peso output</c>).
/// </summary>
/// <remarks>
/// <para>
/// The service owns no file fact and no path: every file resolution, existence check and byte read
/// is delegated to the Documents area (<see cref="IPesoPdfDocumentRead"/>), which resolves the
/// deterministic target from the shared Peso read and the operator-configured base directory. The
/// related-Peso scope is resolved through the Controlo-owned read seam
/// (<see cref="IPesoOutputRead"/> — the real <c>cm_contexts</c> anchors, never a stored
/// <c>jobon_id</c> on the Peso). Nothing is persisted: the projection is composed at read time and
/// no Job On metadata is duplicated (P2-T04 read-transaction rule).</para>
/// <para>
/// Availability mapping (P2-T08 availability rule): an existing file is <c>Available</c>, a missing
/// file is <c>NotGenerated</c> (never an error), and a missing/unusable configured workspace is
/// <c>WorkspaceUnavailable</c> — never conflated with a missing file. A related Peso that vanished
/// between the relation read and the document read is dropped silently: the projection never
/// invents an output for a Peso that no longer exists.</para>
/// </remarks>
public sealed class JobOnControlOutputsService : IJobOnControlOutputsService
{
    private readonly IJobOnRepository _jobOns;
    private readonly IPesoOutputRead _pesoOutputs;
    private readonly IPesoPdfDocumentRead _documents;

    /// <summary>Creates the service over the Job On repository, the Controlo outputs read and the
    /// Documents document read.</summary>
    public JobOnControlOutputsService(
        IJobOnRepository jobOns,
        IPesoOutputRead pesoOutputs,
        IPesoPdfDocumentRead documents)
    {
        ArgumentNullException.ThrowIfNull(jobOns);
        ArgumentNullException.ThrowIfNull(pesoOutputs);
        ArgumentNullException.ThrowIfNull(documents);
        _jobOns = jobOns;
        _pesoOutputs = pesoOutputs;
        _documents = documents;
    }

    /// <inheritdoc />
    public async Task<JobOnControlOutputsResult> GetAsync(
        Guid jobOnId,
        CancellationToken cancellationToken)
    {
        var jobOn = await _jobOns.GetByIdAsync(jobOnId, cancellationToken);
        if (jobOn is null)
        {
            return new JobOnControlOutputsResult.NotFound(jobOnId);
        }

        return new JobOnControlOutputsResult.Found(
            new JobOnControlOutputs(jobOnId, await ComposeOutputsAsync(jobOn, cancellationToken)));
    }

    /// <inheritdoc />
    public async Task<JobOnControlOutputContentResult> ReadAsync(
        Guid jobOnId,
        Guid pesoId,
        CancellationToken cancellationToken)
    {
        var jobOn = await _jobOns.GetByIdAsync(jobOnId, cancellationToken);
        if (jobOn is null)
        {
            return new JobOnControlOutputContentResult.NotFound(jobOnId);
        }

        // The related-peso scope: the Peso must be a production-bound output of THIS occurrence
        // (its cm_id a real cm_contexts row of it). A Peso of another production is never served.
        var related = await RelatedPesoIdsAsync(jobOn, cancellationToken);
        if (!related.Contains(pesoId))
        {
            return new JobOnControlOutputContentResult.RelatedPesoNotFound(jobOnId, pesoId);
        }

        var document = await _documents.ReadAsync(pesoId, cancellationToken);

        return document switch
        {
            PesoPdfContentResult.Found(var fileName, _, var content) =>
                new JobOnControlOutputContentResult.Found(fileName, content),

            PesoPdfContentResult.NotGenerated =>
                new JobOnControlOutputContentResult.NotGenerated(pesoId),

            // Defensive only (the relation read just resolved this Peso): a Peso that vanished is
            // no longer a related output of the occurrence.
            PesoPdfContentResult.NotFound(_) =>
                new JobOnControlOutputContentResult.RelatedPesoNotFound(jobOnId, pesoId),

            PesoPdfContentResult.Refused(var reason, var message) =>
                reason == PesoPdfDocumentReadRefusalReason.ProductionBindingMissing
                    // Defensive only: a production-bound related Peso always has traversal facts;
                    // without a target there is nothing to open — the truthful answer is
                    // "not generated".
                    ? new JobOnControlOutputContentResult.NotGenerated(pesoId)
                    : new JobOnControlOutputContentResult.Refused(Map(reason), message),

            _ => throw new InvalidOperationException("Unknown document content outcome."),
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Composition
    // ---------------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<PesoOutputProjection>> ComposeOutputsAsync(
        DomainJobOn jobOn,
        CancellationToken cancellationToken)
    {
        var outputs = new List<PesoOutputProjection>();

        foreach (var pesoId in await RelatedPesoIdsAsync(jobOn, cancellationToken))
        {
            var document = await _documents.GetAvailabilityAsync(pesoId, cancellationToken);

            switch (document)
            {
                case PesoPdfAvailabilityResult.Available(var fileName, _):
                    outputs.Add(new PesoOutputProjection(
                        pesoId,
                        PesoOutputAvailability.Available,
                        fileName));
                    break;

                case PesoPdfAvailabilityResult.NotGenerated:
                    outputs.Add(new PesoOutputProjection(
                        pesoId,
                        PesoOutputAvailability.NotGenerated,
                        FileName: null));
                    break;

                case PesoPdfAvailabilityResult.NotFound:
                    // The related Peso vanished between the relation read and the document read:
                    // it is no longer a real output of this occurrence; nothing is invented for it.
                    break;

                case PesoPdfAvailabilityResult.Refused(var reason, _):
                    outputs.Add(new PesoOutputProjection(
                        pesoId,
                        reason is PesoPdfDocumentReadRefusalReason.PdfDirectoryNotConfigured
                            or PesoPdfDocumentReadRefusalReason.WorkspaceUnavailable
                            ? PesoOutputAvailability.WorkspaceUnavailable
                            : PesoOutputAvailability.NotGenerated,
                        FileName: null));
                    break;

                default:
                    throw new InvalidOperationException("Unknown document availability outcome.");
            }
        }

        return outputs;
    }

    /// <summary>The related <c>peso_id</c> values of the occurrence, in the stable order of the
    /// Controlo read.</summary>
    private async Task<IReadOnlyList<Guid>> RelatedPesoIdsAsync(
        DomainJobOn jobOn,
        CancellationToken cancellationToken) =>
        await _pesoOutputs.ListByJobOnIdAsync(jobOn.JobOnId.Value, cancellationToken);

    private static JobOnControlOutputRefusalReason Map(PesoPdfDocumentReadRefusalReason reason) =>
        reason switch
        {
            PesoPdfDocumentReadRefusalReason.PdfDirectoryNotConfigured =>
                JobOnControlOutputRefusalReason.PdfDirectoryNotConfigured,
            PesoPdfDocumentReadRefusalReason.WorkspaceUnavailable =>
                JobOnControlOutputRefusalReason.WorkspaceUnavailable,
            PesoPdfDocumentReadRefusalReason.InvalidFileName =>
                JobOnControlOutputRefusalReason.InvalidFileName,
            PesoPdfDocumentReadRefusalReason.ReadFailed =>
                JobOnControlOutputRefusalReason.ReadFailed,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown refusal reason."),
        };
}