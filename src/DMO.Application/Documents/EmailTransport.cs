using System.Net.Mail;

namespace DMO.Application.Documents;

/// <summary>
/// The SMTP transport configuration of the Peso PDF email slice, bound from the
/// <c>Email:Transport</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// This is TRANSPORT configuration only — it never redefines, duplicates or overrides the
/// recipient lists and email templates owned by <c>Controlo_Create → Definições</c> (those remain
/// the single source of truth for recipients/templates; no recipient address is ever hardcoded in
/// code). Nothing is assumed when absent: the transport answers the typed
/// <c>email-transport-not-configured</c> state at send time instead of failing the host startup
/// (email sending is optional operational work, unlike the database/Supabase fail-fast posture).</para>
/// <para>
/// Values are supplied through configuration/environment (<c>Email__Transport__Host</c>,
/// <c>Email__Transport__Port</c>, <c>Email__Transport__Username</c>,
/// <c>Email__Transport__Password</c>, <c>Email__Transport__From</c>,
/// <c>Email__Transport__EnableSsl</c>); no secret is ever committed.</para>
/// </remarks>
public sealed class EmailTransportOptions
{
    /// <summary>Configuration section name bound by the host.</summary>
    public const string SectionName = "Email:Transport";

    /// <summary>The SMTP server host name or address.</summary>
    public string? Host { get; set; }

    /// <summary>The SMTP server port (default 587 when unset and the host is present).</summary>
    public int Port { get; set; } = 587;

    /// <summary>The SMTP authentication user name (optional).</summary>
    public string? Username { get; set; }

    /// <summary>The SMTP authentication password (optional; never logged).</summary>
    public string? Password { get; set; }

    /// <summary>The sender address used on the envelope and the From header.</summary>
    public string? From { get; set; }

    /// <summary>Whether SSL/TLS is requested on connect (defaults to true when unset).</summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>
    /// Whether the transport is considered configured: a host and a sender address are the
    /// irreducible minimum (the port has a default; authentication is optional). No value is
    /// invented when either is absent — the send flow refuses with the typed
    /// <c>email-transport-not-configured</c> state.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(From)
        && Port is > 0 and <= 65535;
}

/// <summary>
/// One outbound email message of the Peso PDF send flow: the resolved recipients, the
/// configuration-owned subject/body and the EXISTING generated document as the attachment.
/// </summary>
/// <remarks>
/// The subject/body are the template's stored text VERBATIM — no placeholder syntax is fixed
/// anywhere in this repository (Q-PLACE/delta §10.4), so no substitution is ever performed. The
/// attachment is the deterministic Peso PDF (the same file the Documents infrastructure stored);
/// the message never carries a filesystem path. The sender address is transport configuration and
/// is never part of the message.</remarks>
public sealed record EmailMessage(
    IReadOnlyList<string> To,
    string Subject,
    string Body,
    string AttachmentFileName,
    byte[] AttachmentBytes);

/// <summary>The typed outcome of one email transport send.</summary>
public enum EmailTransportState
{
    /// <summary>The message was handed to the transport successfully.</summary>
    Sent,

    /// <summary>The transport configuration is absent/incomplete — nothing was attempted.</summary>
    NotConfigured,

    /// <summary>The transport failed (network/auth/server) — the failure never alters the Peso
    /// record, the decision or the generated document.</summary>
    Failed,
}

/// <summary>The typed transport result: the state plus an operator-facing message on failure.</summary>
public sealed record EmailTransportResult(EmailTransportState State, string? Message);

/// <summary>
/// The email transport seam of the Peso PDF send slice: sends one composed message.
/// </summary>
/// <remarks>
/// The application never hardcodes a recipient, a list or a template: everything arrives through
/// the composed message. The transport is replaceable so the orchestration is proven with a
/// recording double while the real SMTP adapter stays a thin configuration-driven adapter.</remarks>
public interface IEmailTransport
{
    /// <summary>Sends the composed message and returns the typed outcome.</summary>
    Task<EmailTransportResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// The <see cref="IEmailTransport"/> over SMTP (BCL <c>SmtpClient</c>), driven exclusively by
/// <see cref="EmailTransportOptions"/>.
/// </summary>
/// <remarks>
/// A missing/incomplete configuration answers <c>not-configured</c> WITHOUT touching the network;
/// any transport failure (DNS/auth/connection/server refusal) is <c>failed</c> with an
/// operator-facing message — never a generic 500 and never a change to the Peso record. The
/// obsolete-API suppression is deliberate: the BCL <c>SmtpClient</c> is the dependency-free
/// transport available in the shared framework (no mail package exists in this repository).</remarks>
public sealed class SmtpEmailTransport : IEmailTransport
{
    private readonly EmailTransportOptions _options;

    /// <summary>Creates the transport over the bound options (plain values — the Application
    /// assembly stays dependency-free; the host binds the configuration section).</summary>
    public SmtpEmailTransport(EmailTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public Task<EmailTransportResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_options.IsConfigured)
        {
            return Task.FromResult(new EmailTransportResult(
                EmailTransportState.NotConfigured,
                "O transporte de email não está configurado (servidor ou remetente em falta); " +
                "configure Email:Transport. Nada foi enviado."));
        }

        // No recipient address is ever assumed: an empty recipient set is refused locally before
        // any network call (the service resolves recipients exclusively from the configured lists).
        if (message.To.Count == 0)
        {
            return Task.FromResult(new EmailTransportResult(
                EmailTransportState.Failed,
                "A mensagem não tem destinatários; nada foi enviado."));
        }

        try
        {
#pragma warning disable SYSLIB0014 // The BCL SmtpClient is the dependency-free transport of this repository.
            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
            };
#pragma warning restore SYSLIB0014

            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                client.Credentials = new System.Net.NetworkCredential(
                    _options.Username,
                    _options.Password ?? string.Empty);
            }

            using var mail = new MailMessage
            {
                From = new MailAddress(_options.From!),
                Subject = message.Subject,
                Body = message.Body,
                IsBodyHtml = false,
            };

            foreach (var address in message.To)
            {
                mail.To.Add(new MailAddress(address));
            }

            using var attachment = new Attachment(
                new MemoryStream(message.AttachmentBytes),
                message.AttachmentFileName,
                "application/pdf");
            mail.Attachments.Add(attachment);

#pragma warning disable SYSLIB0014
            client.Send(mail);
#pragma warning restore SYSLIB0014

            return Task.FromResult(new EmailTransportResult(
                EmailTransportState.Sent,
                Message: null));
        }
        catch (Exception)
        {
            // No logger dependency exists in the Application assembly: the typed failure message
            // travels in the result and the endpoint records status-level observability.
            return Task.FromResult(new EmailTransportResult(
                EmailTransportState.Failed,
                "O envio do email falhou no transporte (servidor, autenticação ou rede); a " +
                "decisão e o Peso não foram alterados."));
        }
    }
}