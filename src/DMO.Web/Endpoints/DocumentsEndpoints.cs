using DMO.Application.Documents;

namespace DMO.Web.Endpoints;

/// <summary>
/// The P2-T08 documents surface of this slice: the Peso PDF generation/storage route, gated by
/// the owning WORKFLOW permission of the operational work (<c>controlo-create</c> — Create owns
/// document generation and the later email/send work, while Approve owns only the decision;
/// Approve's surface stays focused on approve/reject/reopen).
/// </summary>
/// <remarks>
/// <para>
/// The route is read-only on the Peso record: generation never mutates, never bumps and never
/// creates any record — the structured Peso remains the only truth. The response reports the
/// deterministic OUTPUT result (file name + relative target structure) so the operator sees the
/// generated document; no failure body and no success body ever contains the absolute base path,
/// a <c>file:///</c> path, a secret or another user's data (only the convention's relative target
/// is shown). Every refused state is a typed 409 distinguished by its reason token; a missing
/// Peso is a 404.</para>
/// <para>
/// The route is gated by the canonical Controlo Create policy value, never by a new policy; no
/// availability registration and no destination route exists here.</para>
/// </remarks>
public static class DocumentsEndpoints
{
    /// <summary>Base path of the documents surface (inside the Controlo Create working area, next
    /// to the operational work it feeds).</summary>
    public const string DocumentsBasePath = "/controlo/create";

    /// <summary>The canonical policy: the Controlo Create module policy (the owning workflow of
    /// the operational document work).</summary>
    public static string Policy => ControloCreateEndpoints.Policy;

    /// <summary>Non-static logger category marker (static types cannot be generic arguments).</summary>
    public sealed class LoggerCategory;

    /// <summary>Maps the documents endpoints onto the application.</summary>
    public static WebApplication MapDocumentsEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup(DocumentsBasePath)
            .RequireAuthorization(Policy);

        // Route — generate and store the Peso PDF from the authority of ONE peso_id. No body is
        // accepted: the route carries only the canonical identity; there is nothing to bind.
        group.MapPost("/pesos/{pesoId:guid}/peso-pdf", async (
            Guid pesoId,
            IPesoPdfService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GenerateAsync(pesoId, cancellationToken);

            if (result is PesoPdfResult.Refused(var reason, var message))
            {
                logger.LogWarning("Peso PDF refused ({Reason}): {Message}", reason, message);
            }

            return result switch
            {
                PesoPdfResult.Generated(var output) =>
                    Results.Ok(ToResponse("generated", output)),

                PesoPdfResult.AlreadyAvailable(var output) =>
                    Results.Ok(ToResponse("already-available", output)),

                PesoPdfResult.NotFound(var id) =>
                    Results.NotFound(new { reason = "not-found", pesoId = id }),

                PesoPdfResult.ValidationFailed(var errors) =>
                    Results.BadRequest(new { reason = "validation-failed", errors }),

                PesoPdfResult.Refused(var refusalReason, var refusalMessage) =>
                    Results.Conflict(new PesoPdfRefusalResponse(RefusalToken(refusalReason), refusalMessage)),

                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            };
        });

        return app;
    }

    /// <summary>The exact transport token of a Peso PDF refusal (typed, never a generic error).</summary>
    public static string RefusalToken(PesoPdfRefusalReason reason) => reason switch
    {
        PesoPdfRefusalReason.PdfDirectoryNotConfigured => "pdf-directory-not-configured",
        PesoPdfRefusalReason.NotDecided => "not-decided",
        PesoPdfRefusalReason.ProductionBindingMissing => "production-binding-missing",
        PesoPdfRefusalReason.WorkspaceUnavailable => "workspace-unavailable",
        PesoPdfRefusalReason.InvalidFileName => "invalid-file-name",
        PesoPdfRefusalReason.WriteFailed => "write-failed",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown refusal reason."),
    };

    private static PesoPdfResponse ToResponse(string status, PesoPdfOutput output) => new(
        status,
        output.PesoId,
        output.Version,
        output.FileName,
        output.RelativePath,
        output.Bytes);

    /// <summary>The generation outcome shown to the operator (relative convention target only).</summary>
    public sealed record PesoPdfResponse(
        string Status,
        Guid PesoId,
        int Version,
        string FileName,
        string RelativePath,
        long Bytes);

    /// <summary>Typed, actionable refusal response.</summary>
    public sealed record PesoPdfRefusalResponse(string Reason, string Message);
}