using DMO.Application.Documents;

namespace DMO.Web.Endpoints;

/// <summary>
/// The P2-T08 documents surface of this slice: the Peso PDF generation/storage route AND the
/// manual email-send route, both gated by the owning WORKFLOW permission of the operational work
/// (<c>controlo-create</c> — Create owns document generation and the email/send work, while
/// Approve owns only the decision; Approve's surface stays focused on approve/reject/reopen).
/// </summary>
/// <remarks>
/// <para>
/// Both routes are read-only on the Peso record: generation/sending never mutates, never bumps
/// and never creates any record — the structured Peso remains the only truth and a failed send
/// never alters the decision or the <c>peso_id</c>. The responses report the deterministic OUTPUT
/// result and the send evidence (file name + relative convention target + resolved template/
/// recipients) so the operator sees what happened; no failure body and no success body ever
/// contains the absolute base path, a <c>file:///</c> path, a secret or another user's data.
/// Every refused state is a typed 409 distinguished by its reason token; a missing Peso is a
/// 404.</para>
/// <para>
/// The routes are gated by the canonical Controlo Create policy value, never by a new policy; no
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

        // Route — manual email send of the EXISTING Peso PDF (Create-owned operational work; the
        // user decides when to send). The body carries ONLY an optional emailListId (the
        // applicable configured list when configuration does not determine a single one) — no
        // recipient address, no template value and no document content are ever accepted from the
        // client. The transport hands the composed message out; evidence is returned; a failure
        // never alters the approval or the peso_id.
        group.MapPost("/pesos/{pesoId:guid}/peso-pdf/send", async (
            Guid pesoId,
            SendPesoPdfRequest? body,
            IPesoPdfSendService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            var command = new SendPesoPdfCommand(pesoId, body?.EmailListId);
            var result = await service.SendAsync(command, cancellationToken);

            if (result is PesoPdfSendResult.Refused(var reason, var message))
            {
                logger.LogWarning("Peso PDF send refused ({Reason}): {Message}", reason, message);
            }

            return result switch
            {
                PesoPdfSendResult.Sent(var evidence) =>
                    Results.Ok(new PesoPdfSendResponse(
                        "sent",
                        evidence.PesoId,
                        evidence.Version,
                        evidence.FileName,
                        evidence.TemplateName,
                        evidence.Recipients,
                        evidence.SentAt)),

                PesoPdfSendResult.NotFound(var id) =>
                    Results.NotFound(new { reason = "not-found", pesoId = id }),

                PesoPdfSendResult.ValidationFailed(var errors) =>
                    Results.BadRequest(new { reason = "validation-failed", errors }),

                PesoPdfSendResult.Refused(var refusalReason, var refusalMessage) =>
                    Results.Conflict(new PesoPdfRefusalResponse(SendRefusalToken(refusalReason), refusalMessage)),

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

    /// <summary>The exact transport token of a Peso PDF send refusal.</summary>
    public static string SendRefusalToken(PesoPdfSendRefusalReason reason) => reason switch
    {
        PesoPdfSendRefusalReason.PdfDirectoryNotConfigured => "pdf-directory-not-configured",
        PesoPdfSendRefusalReason.NotDecided => "not-decided",
        PesoPdfSendRefusalReason.ProductionBindingMissing => "production-binding-missing",
        PesoPdfSendRefusalReason.WorkspaceUnavailable => "workspace-unavailable",
        PesoPdfSendRefusalReason.InvalidFileName => "invalid-file-name",
        PesoPdfSendRefusalReason.PdfNotGenerated => "pdf-not-generated",
        PesoPdfSendRefusalReason.DocumentReadFailed => "document-read-failed",
        PesoPdfSendRefusalReason.EmailTemplateNotConfigured => "email-template-not-configured",
        PesoPdfSendRefusalReason.EmailTemplateAmbiguous => "email-template-ambiguous",
        PesoPdfSendRefusalReason.EmailListNotConfigured => "email-list-not-configured",
        PesoPdfSendRefusalReason.EmailListSelectionRequired => "email-list-selection-required",
        PesoPdfSendRefusalReason.EmailListNotFound => "email-list-not-found",
        PesoPdfSendRefusalReason.EmailListEmpty => "email-list-empty",
        PesoPdfSendRefusalReason.EmailTransportNotConfigured => "email-transport-not-configured",
        PesoPdfSendRefusalReason.EmailSendFailed => "email-send-failed",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown send refusal reason."),
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

    /// <summary>The manual-send request: ONLY the optional applicable configured list id.</summary>
    public sealed record SendPesoPdfRequest(Guid? EmailListId);

    /// <summary>The send evidence shown to the operator: the attached existing file, the template
    /// used and the resolved recipients (no path, no secret, no address of other users).</summary>
    public sealed record PesoPdfSendResponse(
        string Status,
        Guid PesoId,
        int Version,
        string FileName,
        string TemplateName,
        IReadOnlyList<string> Recipients,
        DateTimeOffset SentAt);

    /// <summary>Typed, actionable refusal response.</summary>
    public sealed record PesoPdfRefusalResponse(string Reason, string Message);
}