using DMO.Application.Controlo.Pesos;
using DMO.Application.Controlo.Settings;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;
using DMO.Domain.Tools;

namespace DMO.Application.Documents;

/// <summary>
/// The manual Peso PDF email-send service contract: Create-owned operational work after the
/// decision. The user decides WHEN to send; the service resolves the group routing
/// AUTOMATICALLY (<c>machine → group B/C → template → list → recipients</c>), attaches the
/// EXISTING generated document and hands it to the transport.
/// </summary>
/// <remarks>
/// The service NEVER regenerates or recalculates the Peso for sending (the existing document is
/// reused); it NEVER writes to the Peso record, NEVER changes the decision and NEVER invents a
/// recipient. No new workflow engine, no manual list selection and no artificial blocking state
/// exist: an unresolvable group routing or an unconfigured transport is a typed refusal.</remarks>
public interface IPesoPdfSendService
{
    /// <summary>Resolves the group routing and sends the existing Peso PDF manually.</summary>
    Task<PesoPdfSendResult> SendAsync(SendPesoPdfCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// The manual Peso PDF send orchestration with the automatic group routing: shared Peso read →
/// machine → group (B1/B2/B3 → B; C1/C2/C3 → C; anything else FAILS CLOSED) → the group's
/// template → its associated recipient list → deterministic target → EXISTING file read →
/// composed message → transport → typed evidence; failures never alter the Peso.
/// </summary>
/// <remarks>
/// <para>
/// The routing is the small, concrete one of this slice (no generic rule architecture): the
/// group's template is the single configured template with <c>machine_group = group</c>; its
/// recipients are the addresses of its associated <c>email_list</c>. Zero templates of the group
/// (or a template without its list — the Definições surface already refuses incomplete routings,
/// this check is the defensive backstop) → <c>email-group-not-configured</c>; more than one →
/// <c>email-template-ambiguous</c> (no precedence rule exists in the model).</para>
/// <para>
/// The subject/body travel VERBATIM (no placeholder syntax exists in this repository — Q-PLACE /
/// delta §10.4) and the evidence is returned plus recorded as status-level application logging
/// only: the current persistence model has no send table and no migration is authorized, so no
/// send ROW is written.</para>
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

        // 5. Machine → group resolution (exact, fail closed): B1/B2/B3 → B; C1/C2/C3 → C; any
        //    machine outside the set is refused without guessing.
        var group = EmailMachineGroupTokens.FromMachine(MachineCode.Parse(production.Machine));
        if (group is not { } resolvedGroup)
        {
            return Refuse(
                PesoPdfSendRefusalReason.MachineGroupUnsupported,
                $"A máquina '{production.Machine}' não pertence a nenhum grupo operacional de " +
                "envio (grupo B: B1/B2/B3; grupo C: C1/C2/C3); nada foi enviado.");
        }

        var groupToken = EmailMachineGroupTokens.ToToken(resolvedGroup);

        // 6. The group's template — the small concrete routing of this slice.
        var template = await ResolveGroupTemplateAsync(resolvedGroup, groupToken, cancellationToken);
        if (template.Refusal is not null)
        {
            return template.Refusal;
        }

        // 7. The template's associated recipients (Definições is the single recipient source).
        var recipients = await ResolveGroupRecipientsAsync(
            template.Template!, groupToken, cancellationToken);
        if (recipients.Refusal is not null)
        {
            return recipients.Refusal;
        }

        // 8. Deterministic target (closed convention); fails closed on unsafe values.
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

        // 9. The EXISTING generated document — reused, never regenerated/recalculated.
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

        // 10. Compose the message (subject/body verbatim; the existing PDF as the attachment).
        var message = new EmailMessage(
            To: recipients.Addresses!,
            Subject: template.Template!.Subject,
            Body: template.Template.Body,
            AttachmentFileName: target.FileName,
            AttachmentBytes: attachment.Bytes);

        // 11. Transport — a failure returns a typed refusal, never a record/decision change.
        var delivered = await _transport.SendAsync(message, cancellationToken);

        return delivered.State switch
        {
            EmailTransportState.Sent => new PesoPdfSendResult.Sent(new PesoPdfSendEvidence(
                sheet.PesoId,
                sheet.Version,
                target.FileName,
                template.Template.Name,
                groupToken,
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
    /// Resolves the single template of the group (the concrete routing: one template per group;
    /// the model fixes no selection precedence beyond the group). Zero templates → the typed
    /// informative <c>email-group-not-configured</c>; more than one → ambiguous.
    /// </summary>
    private async Task<TemplateResolution> ResolveGroupTemplateAsync(
        EmailMachineGroup group,
        string groupToken,
        CancellationToken cancellationToken)
    {
        var templates = (await _templates.ListAsync(cancellationToken))
            .Where(template => template.MachineGroup == group)
            .ToList();

        if (templates.Count == 0)
        {
            return new TemplateResolution(
                null,
                Refuse(
                    PesoPdfSendRefusalReason.EmailGroupNotConfigured,
                    $"O grupo {groupToken} (máquinas {MachinesOf(group)}) não tem template de " +
                    "email configurado; defina o Template do grupo e a lista de destinatários " +
                    "associada em Definições."));
        }

        if (templates.Count > 1)
        {
            return new TemplateResolution(
                null,
                Refuse(
                    PesoPdfSendRefusalReason.EmailTemplateAmbiguous,
                    $"Existem vários templates configurados para o grupo {groupToken} e o modelo " +
                    "não fixa precedência entre eles; deixe exatamente um template por grupo."));
        }

        return new TemplateResolution(templates[0], null);
    }

    /// <summary>
    /// Resolves the recipients of the group's template: the addresses of its associated
    /// configured list (deterministic address ASC). A template without its list (defensive — the
    /// Definições surface refuses incomplete routings) is <c>email-group-not-configured</c>; a
    /// missing list is <c>email-list-not-found</c>; an empty list is refused — no address is
    /// invented.
    /// </summary>
    private async Task<RecipientResolution> ResolveGroupRecipientsAsync(
        EmailTemplate template,
        string groupToken,
        CancellationToken cancellationToken)
    {
        if (template.EmailListId is not { } listId)
        {
            return new RecipientResolution(
                Refuse(
                    PesoPdfSendRefusalReason.EmailGroupNotConfigured,
                    $"O template do grupo {groupToken} não tem lista de destinatários associada; " +
                    "associe a lista em Controlo_Create → Definições."),
                null);
        }

        var list = await _lists.GetByIdAsync(listId.Value, cancellationToken);
        if (list is null)
        {
            return new RecipientResolution(
                Refuse(
                    PesoPdfSendRefusalReason.EmailListNotFound,
                    $"A lista de destinatários associada ao template do grupo {groupToken} já não " +
                    "existe; verifique Definições."),
                null);
        }

        var addresses = list.Recipients
            .Select(recipient => recipient.Address)
            .OrderBy(address => address, StringComparer.Ordinal)
            .ToList();

        if (addresses.Count == 0)
        {
            return new RecipientResolution(
                Refuse(
                    PesoPdfSendRefusalReason.EmailListEmpty,
                    $"A lista '{list.Name}' (grupo {groupToken}) não tem destinatários; acrescente " +
                    "destinatários em Definições."),
                null);
        }

        return new RecipientResolution(null, addresses);
    }

    private static string MachinesOf(EmailMachineGroup group) => group switch
    {
        EmailMachineGroup.B => "B1/B2/B3",
        EmailMachineGroup.C => "C1/C2/C3",
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, "Unknown email machine group."),
    };

    private static PesoPdfSendResult Refuse(PesoPdfSendRefusalReason reason, string message) =>
        new PesoPdfSendResult.Refused(reason, message);

    private sealed record RecipientResolution(
        PesoPdfSendResult? Refusal,
        IReadOnlyList<string>? Addresses);

    private sealed record TemplateResolution(
        EmailTemplate? Template,
        PesoPdfSendResult? Refusal);
}