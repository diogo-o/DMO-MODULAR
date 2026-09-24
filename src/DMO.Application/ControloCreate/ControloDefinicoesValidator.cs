using DMO.Application.Tools;

namespace DMO.Application.ControloCreate;

/// <summary>
/// The exact machine-readable validation error codes of <c>Controlo_Create → Definições</c>
/// (closed set, §26.2).
/// </summary>
/// <remarks>
/// <b>Shared tokens (preserved):</b> <see cref="NameRequired"/>, <see cref="RepairerNotFound"/>
/// and <see cref="MachineUnknown"/> are the contracted transport vocabulary of the repairer
/// family, which moved to <c>Boquilhas > Definições</c> (Owner clarification P2-T07 §34.3 /
/// P2-T05 §31.3); <c>BoquilhasDefinicoesService</c> / <c>BoquilhasDefinicoesEndpoints</c>
/// consume them, so they stay here (F-06 cleanup keeps them; no extraction chosen).
/// </remarks>
public static class ControloDefinicoesValidationErrors
{
    /// <summary>The repairer name was not supplied (blank/whitespace).</summary>
    public const string NameRequired = "NAME_REQUIRED";

    /// <summary>The supplied repairer for an assignment does not exist in the register.</summary>
    public const string RepairerNotFound = "REPAIRER_NOT_FOUND";

    /// <summary>The base directory was not supplied (blank/whitespace).</summary>
    public const string DirectoryRequired = "DIRECTORY_REQUIRED";

    /// <summary>The base directory is not an absolute path.</summary>
    public const string DirectoryInvalid = "DIRECTORY_INVALID";

    /// <summary>The machine code is not one of B1/B2/B3/C1/C2/C3.</summary>
    public const string MachineUnknown = "MACHINE_UNKNOWN";

    /// <summary>The email-list name was not supplied (blank/whitespace).</summary>
    public const string ListNameRequired = "LIST_NAME_REQUIRED";

    /// <summary>A recipient address was not supplied (blank/whitespace).</summary>
    public const string AddressRequired = "ADDRESS_REQUIRED";

    /// <summary>A recipient address does not have the minimal unbroken shape.</summary>
    public const string AddressInvalid = "ADDRESS_INVALID";

    /// <summary>The template name was not supplied (blank/whitespace).</summary>
    public const string TemplateNameRequired = "TEMPLATE_NAME_REQUIRED";

    /// <summary>The template subject was not supplied (blank/whitespace).</summary>
    public const string SubjectRequired = "SUBJECT_REQUIRED";

    /// <summary>The template body was not supplied (blank/whitespace).</summary>
    public const string BodyRequired = "BODY_REQUIRED";

    /// <summary>The template document type is not one of peso/pegamentos/resumo.</summary>
    public const string DocumentTypeUnknown = "DOCUMENT_TYPE_UNKNOWN";

    /// <summary>The delete was not explicitly confirmed.</summary>
    public const string DeleteNotConfirmed = "DELETE_NOT_CONFIRMED";

    /// <summary>
    /// The glass-density update targeted a processo that is not one of the canonical tokens
    /// (<c>NNPB</c>/<c>PS</c>) — correction contract §5.3.
    /// </summary>
    public const string ProcessoUnknown = "PROCESSO_UNKNOWN";

    /// <summary>
    /// The glass-density update carried a density that is not strictly positive (<c>≤ 0</c>) —
    /// correction contract §5.3/R7.
    /// </summary>
    public const string DensityNotPositive = "DENSITY_NOT_POSITIVE";
}

/// <summary>
/// Pure static Definições validator: it runs before any write and returns the exact contracted
/// codes (§12.2, §13.2, §14.2, correction §5.3).
/// </summary>
public static class ControloDefinicoesValidator
{
    /// <summary>Validates the PDF-directory configure/change command.</summary>
    public static IReadOnlyList<string> Validate(SetPdfDirectoryCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(command.BaseDirectory))
        {
            errors.Add(ControloDefinicoesValidationErrors.DirectoryRequired);
        }
        else if (!Path.IsPathRooted(command.BaseDirectory.Trim()))
        {
            // The configured path is a server-host absolute filesystem path (Q-PDF); a non-absolute
            // value is refused here so only absolute paths reach the accessibility check.
            errors.Add(ControloDefinicoesValidationErrors.DirectoryInvalid);
        }

        return errors;
    }

    /// <summary>Validates the email-list create command.</summary>
    public static IReadOnlyList<string> Validate(CreateEmailListCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ValidateList(command.Name, command.Recipients);
    }

    /// <summary>Validates the email-list update command.</summary>
    public static IReadOnlyList<string> Validate(UpdateEmailListCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ValidateList(command.Name, command.Recipients).ToList();

        if (command.EmailListId == Guid.Empty)
        {
            errors.Add(ControloDefinicoesValidationErrors.ListNameRequired);
        }

        return errors;
    }

    /// <summary>Validates the email-list delete command (explicit confirmation).</summary>
    public static IReadOnlyList<string> Validate(DeleteEmailListCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.DeleteConfirmed
            ? []
            : [ControloDefinicoesValidationErrors.DeleteNotConfirmed];
    }

    /// <summary>Validates the email-template create command.</summary>
    public static IReadOnlyList<string> Validate(CreateEmailTemplateCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ValidateTemplate(command.Name, command.Subject, command.Body, command.DocumentType);
    }

    /// <summary>Validates the email-template update command.</summary>
    public static IReadOnlyList<string> Validate(UpdateEmailTemplateCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ValidateTemplate(
            command.Name,
            command.Subject,
            command.Body,
            command.DocumentType).ToList();

        if (command.EmailTemplateId == Guid.Empty)
        {
            errors.Add(ControloDefinicoesValidationErrors.TemplateNameRequired);
        }

        return errors;
    }

    /// <summary>Validates the email-template delete command (explicit confirmation).</summary>
    public static IReadOnlyList<string> Validate(DeleteEmailTemplateCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.DeleteConfirmed
            ? []
            : [ControloDefinicoesValidationErrors.DeleteNotConfirmed];
    }

    /// <summary>
    /// Validates the glass-density update command (correction contract §5.3): the path processo
    /// must be exactly the canonical <c>NNPB</c>/<c>PS</c> token and the density must be strictly
    /// positive. No alias, no case folding and no other processo is accepted.
    /// </summary>
    public static IReadOnlyList<string> Validate(UpdateGlassDensityCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(command.Processo)
            || ToolTokens.ParseProcesso(command.Processo.Trim()) is null)
        {
            errors.Add(ControloDefinicoesValidationErrors.ProcessoUnknown);
        }

        if (command.DensityGCm3 <= 0)
        {
            errors.Add(ControloDefinicoesValidationErrors.DensityNotPositive);
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateList(string name, IReadOnlyList<string> recipients)
    {
        ArgumentNullException.ThrowIfNull(recipients);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(ControloDefinicoesValidationErrors.ListNameRequired);
        }

        foreach (var address in recipients)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                errors.Add(ControloDefinicoesValidationErrors.AddressRequired);
            }
            else if (!IsMinimalAddressShape(address.Trim()))
            {
                errors.Add(ControloDefinicoesValidationErrors.AddressInvalid);
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateTemplate(
        string name,
        string subject,
        string body,
        string? documentType)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(ControloDefinicoesValidationErrors.TemplateNameRequired);
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            errors.Add(ControloDefinicoesValidationErrors.SubjectRequired);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            errors.Add(ControloDefinicoesValidationErrors.BodyRequired);
        }

        if (documentType is not null)
        {
            var token = documentType.Trim();
            var known = token switch
            {
                "peso" or "pegamentos" or "resumo" => true,
                _ => false,
            };

            if (!known)
            {
                errors.Add(ControloDefinicoesValidationErrors.DocumentTypeUnknown);
            }
        }

        return errors;
    }

    /// <summary>
    /// The minimal unbroken address shape (Q-ADDR): exactly one <c>@</c>, non-blank local part and
    /// domain, no whitespace. No full RFC validation is invented.
    /// </summary>
    public static bool IsMinimalAddressShape(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var trimmed = address.Trim();

        if (trimmed != address || trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var separator = trimmed.IndexOf('@');
        if (separator <= 0 || separator != trimmed.LastIndexOf('@'))
        {
            return false;
        }

        return separator < trimmed.Length - 1;
    }
}