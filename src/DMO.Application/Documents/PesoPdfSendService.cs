using DMO.Application.ControloCreate;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;

namespace DMO.Application.Documents;

/// <summary>
/// The manual Peso PDF email-send service contract: Create-owned operational work after the
/// decision. The user decides WHEN to send; the service resolves the applicable configured
/// template/list, attaches the EXISTING generated document and hands it to the transport.
/// </summary>
/// <remarks>
/// The service NEVER regenerates or recalculates the Peso for sending (the existing document is
/// reused); it NEVER writes to the Peso record, NEVER changes the decision and NEVER invents a
/// recipient. No new workflow engine and no artificial blocking state exist: an unresolvable
/// template/list or an unconfigured transport is a typed refusal, not a workflow.</remarks>
public interface IPesoPdfSendService
{
    /// <summary>Resolves the applicable configuration and sends the existing Peso PDF manually.</summary>
    Task<PesoPdfSendResult> SendAsync(SendPesoPdfCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// The manual Peso PDF send orchestration: shared Peso read → deterministic target → EXISTING
/// file read → configured template/list resolution (no hardcoded recipients, no second source of
/// truth) → composed message → transport → typed evidence; failures never alter the Peso.
/// </summary>
/// <remarks>
/// <para>
/// Template resolution (the ONLY applicability rule the current model fixes — Q-DOCTYPE): the
/// <c>peso</c> template wins; absent that, a <c>generic</c> (document_type NULL) template is used.
/// If more than one template still applies at that precedence, the send refuses
/// <c>email-template-ambiguous</c> (the model fixes no selection-precedence beyond applicability —
/// delta §9.4/§10.5). List resolution: an OPED operator selection is honoured; with none, exactly
/// one configured list is used automatically, and more than one requires an explicit choice
/// (<c>email-list-selection-required</c>) — the app avoids re-asking for what configuration
/// already determines but never guesses among several lists.</para>
/// <para>
/// The subject/body travel VERBATIM (no placeholder syntax exists in this repository — Q-PLACE /
/// delta §10.4, so no substitution is performed). The evidence is returned and recorded as
/// status-level application logging only: the current persistence model has no send table and no
/// migration is authorized, so no send ROW is written.</para>
/// </remarks>
public sealed class PesoPdfSendService : IPesoPdfSendService
{
    private readonly IPdfDirectorySettingsRepository _pdfDirectory;
    private readonly IPdfDirectoryProbe _directoryProbe;
    private readonly IControloCreateService _create;
    private readonly IPesoPdfFileStore _files;
    private readonly IEmailTemplateRepository _templates;
    private readonly IEmailListRepository _lists;
    private readonly IEmailTransport _transport;

    /// <summary>Creates the send service over the settings, the shared read, the file store, the
    /// configured template/list repositories and the transport.</summary>
    public PesoPdfSendService(
        IPdfDirectorySettingsRepository pdfDirectory,
        IPdfDirectoryProbe directoryProbe,
        IControloCreateService create,
        IPesoPdfFileStore files,
        IEmailTemplateRepository templates,
        IEmailListRepository lists,
        IEmailTransport transport)
    {
        ArgumentNullException.ThrowIfNull(pdfDirectory);
        ArgumentNullException.ThrowIfNull(directoryProbe);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(transport);
        _pdfDirectory = pdfDirectory;
        _directoryProbe = directoryProbe;
        _create = create;
        _files = files;
        _templates = templates;
        _lists = lists;
        _transport = transport;
    }

    /// <inheritdoc />
    public async Task<PesoPdfSendResult> SendAsync(
        SendPesoPdfCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1. Configured base directory — an absent row is the explicit not-configured state.
        var settings = await _pdfDirectory.GetAsync(cancellationToken);
        if (settings is null)
        {
            return Refuse(
                PesoPdfSendRefusalReason.PdfDirectoryNotConfigured,
                "Não existe diretório base configurado; configure-o em " +
                "Controlo_Create → Definições antes de enviar.");
        }

        // 2. Workspace probed before any read/write (server-host semantics).
        if (_directoryProbe.Probe(settings.BaseDirectory) != PdfDirectoryCheckState.Ok)
        {
            return Refuse(
                PesoPdfSendRefusalReason.WorkspaceUnavailable,
                "O diretório base configurado não está acessível ao servidor; verifique Definições.");
        }

        // 3. The SHARED read model — the same peso_id read Create renders.
        var read = await _create.GetAsync(command.PesoId, cancellationToken);
        if (read is PesoResult.NotFound)
        {
            return new PesoPdfSendResult.NotFound(command.PesoId);
        }

        if (read is not PesoResult.Found(var sheet))
        {
            return new PesoPdfSendResult.ValidationFailed(["PESO_READ_FAILED"]);
        }

        // 4. Decided + production-bound preconditions (Create owns the work after the decision).
        var status = PesoStatusTokens.Parse(sheet.Status);
        if (status is not (PesoStatus.Aprovado or PesoStatus.NaoAprovado))
        {
            return Refuse(
                PesoPdfSendRefusalReason.NotDecided,
                "Este Peso ainda não foi decidido; o envio só é apresentado após a decisão.");
        }

        if (sheet.Production is not { } production)
        {
            return Refuse(
                PesoPdfSendRefusalReason.ProductionBindingMissing,
                "Este Peso não está associado a uma produção; não existe documento para enviar.");
        }

        // 5. Deterministic target (closed convention); fails closed on unsafe values.
        if (!PesoPdfNaming.TryCompose(
                production.Reference,
                production.ProductionNumber,
                production.Machine,
                out var target)
            || target is null)
        {
            return Refuse(
                PesoPdfSendRefusalReason.InvalidFileName,
                "A referência, o número de produção ou a máquina não permitem formar o destino; " +
                "nada foi enviado.");
        }

        // 6. The EXISTING generated document — reused, never regenerated/recalculated.
        var attachment = await _files.ReadAsync(
            settings.BaseDirectory,
            target.RelativeDirectory,
            target.FileName,
            cancellationToken);

        if (attachment.State is PesoPdfFileReadState.Missing)
        {
            return Refuse(
                PesoPdfSendRefusalReason.PdfNotGenerated,
                "O PDF deste Peso ainda não foi gerado; gere-o primeiro (nada é recalculado ou " +
                "regenerado implicitamente para envio).");
        }

        if (attachment.State is not PesoPdfFileReadState.Found || attachment.Bytes is null)
        {
            return Refuse(
                PesoPdfSendRefusalReason.DocumentReadFailed,
                "Não foi possível ler o PDF existente; repita ou verifique o diretório em Definições.");
        }

        // 7. Template resolution — the configured data is the single source of truth.
        var template = await ResolveTemplateAsync(cancellationToken);
        if (template.Refusal is not null)
        {
            return template.Refusal;
        }

        // 8. Recipient resolution — from the configured lists only (never hardcoded; the operator
        //    chooses only when configuration does not determine it).
        var recipients = await ResolveRecipientsAsync(command.EmailListId, cancellationToken);
        if (recipients.Refusal is not null)
        {
            return recipients.Refusal;
        }

        // 9. Compose the message (subject/body verbatim; the existing PDF as the attachment).
        var message = new EmailMessage(
            To: recipients.Addresses!,
            Subject: template.Template!.Subject,
            Body: template.Template.Body,
            AttachmentFileName: target.FileName,
            AttachmentBytes: attachment.Bytes);

        // 10. Transport — a failure returns a typed refusal, never a record/decision change.
        var delivered = await _transport.SendAsync(message, cancellationToken);

        return delivered.State switch
        {
            EmailTransportState.Sent => new PesoPdfSendResult.Sent(new PesoPdfSendEvidence(
                sheet.PesoId,
                sheet.Version,
                target.FileName,
                template.Template.Name,
                recipients.Addresses!,
                DateTimeOffset.UtcNow)),

            EmailTransportState.NotConfigured => Refuse(
                PesoPdfSendRefusalReason.EmailTransportNotConfigured,
                delivered.Message ?? "O transporte de email não está configurado; nada foi enviado."),

            _ => Refuse(
                PesoPdfSendRefusalReason.EmailSendFailed,
                delivered.Message ?? "O envio do email falhou; a decisão e o Peso não foram alterados."),
        };
    }

    /// <summary>
    /// Resolves the applicable configured template: the <c>peso</c> template wins; otherwise a
    /// generic (document_type NULL) template is used. More than one template at the winning
    /// precedence is AMBIGUOUS (the model fixes no further precedence — delta §9.4/§10.5).
    /// </summary>
    private async Task<TemplateResolution> ResolveTemplateAsync(CancellationToken cancellationToken)
    {
        var all = await _templates.ListAsync(cancellationToken);

        var peso = all
            .Where(template => template.DocumentType == EmailTemplateDocumentType.Peso)
            .ToList();
        var generic = all
            .Where(template => template.DocumentType is null)
            .ToList();

        if (peso.Count == 1)
        {
            return new TemplateResolution(peso[0], null);
        }

        if (peso.Count > 1)
        {
            return new TemplateResolution(
                null,
                Refuse(PesoPdfSendRefusalReason.EmailTemplateAmbiguous,
                    "Existem vários templates 'peso' configurados e o modelo atual não fixa " +
                    "precedência entre eles; deixe exatamente um template 'peso' (ou um genérico)."));
        }

        return generic.Count == 1
            ? new TemplateResolution(generic[0], null)
            : new TemplateResolution(
                null,
                Refuse(PesoPdfSendRefusalReason.EmailTemplateNotConfigured,
                    "Não existe template de email aplicável ao Peso (defina um template 'peso' ou " +
                    "genérico em Definições)."));
    }

    /// <summary>
    /// Resolves the recipient set from the configured lists: an OPED selection is honoured; with
    /// none, exactly one configured list is used automatically (the app avoids re-asking for what
    /// configuration determines); more than one list requires an explicit choice. Addresses are
    /// deterministic (address ASC). An empty list is refused — no address is invented.
    /// </summary>
    private async Task<RecipientResolution> ResolveRecipientsAsync(
        Guid? emailListId,
        CancellationToken cancellationToken)
    {
        var lists = await _lists.ListAsync(cancellationToken);

        EmailList? chosen;
        if (emailListId is { } selectedId)
        {
            chosen = lists.FirstOrDefault(list => list.EmailListId.Value == selectedId);

            return chosen is null
                ? new RecipientResolution(
                    Refuse(PesoPdfSendRefusalReason.EmailListNotFound,
                        "A lista de destinatários selecionada não existe; verifique Definições."),
                    null)
                : CheckRecipients(chosen);
        }

        var remaining = lists.Count;

        if (remaining == 0)
        {
            return new RecipientResolution(
                Refuse(PesoPdfSendRefusalReason.EmailListNotConfigured,
                    "Não existe lista de destinatários configurada; defina uma em Definições " +
                    "(nada é inventado)."),
                null);
        }

        if (remaining > 1)
        {
            return new RecipientResolution(
                Refuse(PesoPdfSendRefusalReason.EmailListSelectionRequired,
                    "Existem várias listas de destinatários configuradas; selecione a lista " +
                    "aplicável (a configuração atual não determina automaticamente uma única)."),
                null);
        }

        return CheckRecipients(lists[0]);
    }

    private static RecipientResolution CheckRecipients(EmailList list)
    {
        var addresses = list.Recipients
            .Select(recipient => recipient.Address)
            .OrderBy(address => address, StringComparer.Ordinal)
            .ToList();

        if (addresses.Count == 0)
        {
            return new RecipientResolution(
                Refuse(PesoPdfSendRefusalReason.EmailListEmpty,
                    $"A lista '{list.Name}' não tem destinatários; acrescente destinatários em Definições."),
                null);
        }

        return new RecipientResolution(null, addresses);
    }

    private static PesoPdfSendResult Refuse(PesoPdfSendRefusalReason reason, string message) =>
        new PesoPdfSendResult.Refused(reason, message);

    private sealed record RecipientResolution(
        PesoPdfSendResult? Refusal,
        IReadOnlyList<string>? Addresses);

    private sealed record TemplateResolution(
        EmailTemplate? Template,
        PesoPdfSendResult? Refusal);
}