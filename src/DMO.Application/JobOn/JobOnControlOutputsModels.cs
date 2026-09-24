namespace DMO.Application.JobOn;

/// <summary>
/// The availability of ONE Peso output of a Job On occurrence (the outputs projection of this
/// slice: <c>jobon_id → peso_id → Peso output</c>).
/// </summary>
/// <remarks>
/// <see cref="Available"/> means the deterministic Peso PDF exists at the configured document
/// target; <see cref="NotGenerated"/> means the Peso exists but its PDF has not been generated yet;
/// <see cref="WorkspaceUnavailable"/> means the configured document workspace is absent or not
/// usable — deliberately distinguishable from a missing file (P2-T08 availability rule). The
/// projection is composed at read time and is never persisted: no Job On metadata is duplicated.
/// </remarks>
public enum PesoOutputAvailability
{
    /// <summary>The Peso PDF exists at the deterministic target — "Disponível".</summary>
    Available,

    /// <summary>The Peso exists but its PDF has not been generated yet — "Ainda não gerado".</summary>
    NotGenerated,

    /// <summary>The configured document workspace is absent or not usable — distinguishable from a
    /// missing file.</summary>
    WorkspaceUnavailable,
}

/// <summary>
/// One Peso output of a Job On occurrence: the identifier needed to open the document
/// (<see cref="PesoId"/> — the route identity), the availability and the deterministic file name
/// when available.
/// </summary>
/// <remarks>
/// Read-time projection only: nothing here is persisted and no absolute path is ever carried — the
/// file name is a presentation fact resolved by the Documents area from the configured base
/// directory, and the open action always goes through the controlled application route keyed by
/// <see cref="PesoId"/>.
/// </remarks>
public sealed record PesoOutputProjection(
    Guid PesoId,
    PesoOutputAvailability Availability,
    string? FileName);

/// <summary>The outputs projection of ONE Job On occurrence: the related Peso outputs in the stable
/// order supplied by the Controlo read (creation order — no ranking, no merging).</summary>
public sealed record JobOnControlOutputs(
    Guid JobOnId,
    IReadOnlyList<PesoOutputProjection> Outputs);

/// <summary>The closed result set of the Job On control-outputs read.</summary>
public abstract record JobOnControlOutputsResult
{
    private JobOnControlOutputsResult()
    {
    }

    /// <summary>The occurrence does not exist.</summary>
    public sealed record NotFound(Guid JobOnId) : JobOnControlOutputsResult;

    /// <summary>The occurrence and its related Peso outputs.</summary>
    public sealed record Found(JobOnControlOutputs Value) : JobOnControlOutputsResult;
}

/// <summary>The typed refusal vocabulary of the Peso PDF open read on the Job On surface.</summary>
public enum JobOnControlOutputRefusalReason
{
    /// <summary>No base directory is configured in Controlo_Create → Definições.</summary>
    PdfDirectoryNotConfigured,

    /// <summary>The configured workspace is not usable from the server process — distinguishable
    /// from a missing file.</summary>
    WorkspaceUnavailable,

    /// <summary>A reference/production/machine value cannot form a safe document target.</summary>
    InvalidFileName,

    /// <summary>The stored file could not be read for another infrastructure reason.</summary>
    ReadFailed,
}

/// <summary>The closed result set of the Peso PDF open read of ONE related Peso.</summary>
/// <remarks>
/// <see cref="Found"/> carries the file name and the stored bytes — the endpoint streams them
/// inline; every other outcome is typed so the open action is never a dead button and an
/// unrelated Peso is never served (the related-peso scope is resolved against the real
/// <c>cm_contexts</c> of the occurrence — the application never guesses).
/// </remarks>
public abstract record JobOnControlOutputContentResult
{
    private JobOnControlOutputContentResult()
    {
    }

    /// <summary>The stored bytes of the Peso PDF of a related Peso.</summary>
    public sealed record Found(string FileName, byte[] Content) : JobOnControlOutputContentResult;

    /// <summary>The Peso exists but its PDF has not been generated yet.</summary>
    public sealed record NotGenerated(Guid PesoId) : JobOnControlOutputContentResult;

    /// <summary>The occurrence does not exist.</summary>
    public sealed record NotFound(Guid JobOnId) : JobOnControlOutputContentResult;

    /// <summary>The occurrence exists but the Peso is not one of its related outputs.</summary>
    public sealed record RelatedPesoNotFound(Guid JobOnId, Guid PesoId) : JobOnControlOutputContentResult;

    /// <summary>A typed configuration/workspace/infrastructure refusal; never conflated with a
    /// missing file.</summary>
    public sealed record Refused(JobOnControlOutputRefusalReason Reason, string Message) : JobOnControlOutputContentResult;
}