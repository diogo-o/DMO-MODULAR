using DMO.Application.Access;
using DMO.Application.Controlo.Settings;
using DMO.Domain.Controlo;
using DMO.Web.Authorization;

namespace DMO.Web.Endpoints.Controlo;

/// <summary>
/// Minimal API surface of <c>Controlo_Create → Definições</c> (P2-T05 contract §21.3 routes
/// 15–17 — the PDF directory, the email lists and the email templates; route 12 is the Razor page
/// <c>Pages/Controlo/Definicoes</c>; post-closure glass-density correction contract §5.3 routes
/// 18–19).
/// </summary>
/// <remarks>
/// <para>
/// Every Definições route/action carries EXACTLY the same canonical Module policy as every other
/// P2-T05 route — <c>dmo.module.controlo-create</c> — and no other. A <c>controlo-approve</c>-only
/// caller is denied every one of them server-side (§21.5, AC-G2); the shared <c>controlo</c>
/// destination never merges grants. Definições is a surface inside the Controlo Create working
/// area — not a destination, not a Module, not registered anywhere (§9.1, AC-Y1).</para>
/// <para>
/// <b>Superseded (Owner clarification P2-T07 §34.3 / P2-T05 §31.3):</b> the repairer register and
/// the machine → repairer assignment routes (the old routes 13/14) are REMOVED from this surface —
/// the repairer family now belongs to <c>Boquilhas > Definições</c> (the physical
/// <c>repairers</c>/<c>machine_repairer_assignments</c> tables stay where they are; the Boquilhas
/// surface owns their operation). The PDF/email/document settings are NOT moved and remain here.</para>
/// <para>
/// The endpoints perform no domain decision: they bind transport shapes, call the application
/// service and switch on the closed settings result set. Every persisted-state refusal is a 409
/// distinguished by its reason token; no failure body contains a secret, a path beyond the
/// configured directory value itself, a connection string or another user's data.</para>
/// <para>
/// Routes 18/19 (glass densities): <c>GET /glass-densities</c> returns the two current
/// operational values (always exactly NNPB and PS, with versions); <c>PUT
/// /glass-densities/{{processo}}</c> updates ONLY that processo's row, version-guarded
/// (<c>stale-version</c> 409, nothing written), refusing unknown processos with
/// <c>PROCESSO_UNKNOWN</c> and non-positive values with <c>DENSITY_NOT_POSITIVE</c>.</para>
/// </remarks>
public static class ControloDefinicoesEndpoints
{
    /// <summary>Base path of the Definições surface inside Controlo Create.</summary>
    public const string DefinicoesBasePath = "/controlo/create/definicoes";

    /// <summary>
    /// The fixed canonical process entries of the glass-density surface (Owner rule): exactly
    /// NNPB and PS, in the canonical order — the same tokens as <c>tools_processo_check</c> and
    /// the domain <c>Processo</c> enum. No generic catalog and no other processo exists.
    /// </summary>
    public static readonly IReadOnlyList<string> CanonicalProcessos = ["NNPB", "PS"];

    /// <summary>Non-static logger category marker (static types cannot be generic arguments).</summary>
    public sealed class LoggerCategory;

    /// <summary>Maps the Definições endpoints onto the application.</summary>
    public static WebApplication MapControloDefinicoesEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup(DefinicoesBasePath)
            .RequireAuthorization(ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.ControloCreate));

        // Route 15 — PDF directory: read (not-configured is explicit) / configure-change / check
        // (server-side, typed vocabulary only; the browser only edits/submits the configuration).
        group.MapGet("/pdf-directory", async (
            IControloDefinicoesService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetPdfDirectoryAsync(cancellationToken);

            return result is SettingsResult.PdfDirectoryFound(var view)
                ? Results.Ok(new PdfDirectoryResponse(
                    view is null ? null : view.BaseDirectory,
                    view?.Version))
                : MapResult(result);
        });

        group.MapPut("/pdf-directory", async (
            SetPdfDirectoryRequest? body,
            IControloDefinicoesService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(ControloDefinicoesValidationErrors.DirectoryRequired);
            }

            var command = new SetPdfDirectoryCommand(body.BaseDirectory ?? string.Empty, body.ExpectedVersion);

            return await ExecuteAsync(
                token => service.SetPdfDirectoryAsync(command, token),
                logger,
                cancellationToken,
                success: result => result is SettingsResult.PdfDirectorySaved(var version)
                    ? Results.Ok(new PdfDirectorySavedResponse(version))
                    : null);
        });

        group.MapPost("/pdf-directory/check", async (
            IControloDefinicoesService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CheckPdfDirectoryAsync(cancellationToken);

            return result is SettingsResult.PdfDirectoryCheck(var check)
                ? Results.Ok(new PdfDirectoryCheckResponse(
                    CheckToken(check.State),
                    check.Message))
                : MapResult(result);
        });

        // Route 16 — email lists: list / create / update (name + complete recipient set) / delete
        // with explicit confirmation.
        group.MapGet("/email-lists", async (
            IControloDefinicoesService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListEmailListsAsync(cancellationToken);

            return result is SettingsResult.EmailListsFound(var lists)
                ? Results.Ok(new EmailListsResponse(
                    lists
                        .Select(list => new EmailListItemResponse(
                            list.EmailListId,
                            list.Name,
                            list.RecipientCount,
                            list.Version))
                        .ToArray()))
                : MapResult(result);
        });

        group.MapPost("/email-lists", async (
            CreateEmailListRequest? body,
            IControloDefinicoesService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(ControloDefinicoesValidationErrors.ListNameRequired);
            }

            var command = new CreateEmailListCommand(
                body.Name ?? string.Empty,
                body.Recipients ?? []);

            return await ExecuteAsync(
                token => service.CreateEmailListAsync(command, token),
                logger,
                cancellationToken,
                success: result => result is SettingsResult.EmailListCreated(var id, var version)
                    ? Results.Created(
                        $"{DefinicoesBasePath}/email-lists/{id}",
                        new EmailListCreatedResponse(id, version))
                    : null);
        });

        group.MapGet("/email-lists/{emailListId:guid}", async (
            Guid emailListId,
            IControloDefinicoesService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetEmailListAsync(emailListId, cancellationToken);

            return result is SettingsResult.EmailListFound(var list)
                ? Results.Ok(new EmailListResponse(
                    list.EmailListId.Value,
                    list.Name,
                    list.Version,
                    list.Recipients.Select(recipient => recipient.Address).ToArray()))
                : MapResult(result);
        });

        group.MapPut("/email-lists/{emailListId:guid}", async (
            Guid emailListId,
            UpdateEmailListRequest? body,
            IControloDefinicoesService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(ControloDefinicoesValidationErrors.ListNameRequired);
            }

            var command = new UpdateEmailListCommand(
                emailListId,
                body.ExpectedVersion,
                body.Name ?? string.Empty,
                body.Recipients ?? []);

            return await ExecuteAsync(
                token => service.UpdateEmailListAsync(command, token),
                logger,
                cancellationToken,
                success: result => result is SettingsResult.EmailListUpdated(var id, var version)
                    ? Results.Ok(new EmailListUpdatedResponse(id, version))
                    : null);
        });

        group.MapDelete("/email-lists/{emailListId:guid}", async (
            Guid emailListId,
            int? expectedVersion,
            bool? deleteConfirmed,
            IControloDefinicoesService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            var command = new DeleteEmailListCommand(
                emailListId,
                expectedVersion ?? 0,
                deleteConfirmed ?? false);

            return await ExecuteAsync(
                token => service.DeleteEmailListAsync(command, token),
                logger,
                cancellationToken,
                success: result => result is SettingsResult.EmailListDeleted
                    ? Results.NoContent()
                    : null);
        });

        // Route 17 — email templates: list / create / update / delete (verbatim text; no
        // placeholder parsing — Q-PLACE).
        group.MapGet("/email-templates", async (
            IControloDefinicoesService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListEmailTemplatesAsync(cancellationToken);

            return result is SettingsResult.EmailTemplatesFound(var templates)
                ? Results.Ok(new EmailTemplatesResponse(
                    templates
                        .Select(template => new EmailTemplateItemResponse(
                            template.EmailTemplateId.Value,
                            template.Name,
                            template.Subject,
                            template.Body,
                            template.DocumentType is { } type
                                ? EmailTemplateDocumentTypeTokens.ToToken(type)
                                : null,
                            template.MachineGroup is { } group
                                ? EmailMachineGroupTokens.ToToken(group)
                                : null,
                            template.EmailListId?.Value,
                            template.Version))
                        .ToArray()))
                : MapResult(result);
        });

        group.MapPost("/email-templates", async (
            CreateEmailTemplateRequest? body,
            IControloDefinicoesService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(ControloDefinicoesValidationErrors.TemplateNameRequired);
            }

            var command = new CreateEmailTemplateCommand(
                body.Name ?? string.Empty,
                body.Subject ?? string.Empty,
                body.Body ?? string.Empty,
                body.DocumentType,
                body.MachineGroup,
                body.EmailListId);

            return await ExecuteAsync(
                token => service.CreateEmailTemplateAsync(command, token),
                logger,
                cancellationToken,
                success: result => result is SettingsResult.EmailTemplateCreated(var id, var version)
                    ? Results.Created(
                        $"{DefinicoesBasePath}/email-templates/{id}",
                        new EmailTemplateCreatedResponse(id, version))
                    : null);
        });

        group.MapGet("/email-templates/{emailTemplateId:guid}", async (
            Guid emailTemplateId,
            IControloDefinicoesService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetEmailTemplateAsync(emailTemplateId, cancellationToken);

            return result is SettingsResult.EmailTemplateFound(var template)
                ? Results.Ok(new EmailTemplateResponse(
                    template.EmailTemplateId.Value,
                    template.Name,
                    template.Subject,
                    template.Body,
                    template.DocumentType is { } type
                        ? EmailTemplateDocumentTypeTokens.ToToken(type)
                        : null,
                    template.MachineGroup is { } group
                        ? EmailMachineGroupTokens.ToToken(group)
                        : null,
                    template.EmailListId?.Value,
                    template.Version))
                : MapResult(result);
        });

        group.MapPut("/email-templates/{emailTemplateId:guid}", async (
            Guid emailTemplateId,
            UpdateEmailTemplateRequest? body,
            IControloDefinicoesService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(ControloDefinicoesValidationErrors.TemplateNameRequired);
            }

            var command = new UpdateEmailTemplateCommand(
                emailTemplateId,
                body.ExpectedVersion,
                body.Name ?? string.Empty,
                body.Subject ?? string.Empty,
                body.Body ?? string.Empty,
                body.DocumentType,
                body.MachineGroup,
                body.EmailListId);

            return await ExecuteAsync(
                token => service.UpdateEmailTemplateAsync(command, token),
                logger,
                cancellationToken,
                success: result => result is SettingsResult.EmailTemplateUpdated(var id, var version)
                    ? Results.Ok(new EmailTemplateUpdatedResponse(id, version))
                    : null);
        });

        group.MapDelete("/email-templates/{emailTemplateId:guid}", async (
            Guid emailTemplateId,
            int? expectedVersion,
            bool? deleteConfirmed,
            IControloDefinicoesService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            var command = new DeleteEmailTemplateCommand(
                emailTemplateId,
                expectedVersion ?? 0,
                deleteConfirmed ?? false);

            return await ExecuteAsync(
                token => service.DeleteEmailTemplateAsync(command, token),
                logger,
                cancellationToken,
                success: result => result is SettingsResult.EmailTemplateDeleted
                    ? Results.NoContent()
                    : null);
        });

        // Route 18/19 — glass densities (post-closure correction contract §5.3): GET returns the
        // two CURRENT operational values (always exactly NNPB and PS, with versions); PUT updates
        // ONLY the targeted processo's row, version-guarded.
        group.MapGet("/glass-densities", async (
            IControloDefinicoesService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListGlassDensitiesAsync(cancellationToken);

            return result is SettingsResult.GlassDensitiesFound(var densities)
                ? Results.Ok(new GlassDensitiesResponse(
                    CanonicalProcessos
                        .Select(processo => densities.FirstOrDefault(setting => setting.Processo == processo))
                        .Where(setting => setting is not null)
                        .Select(setting => new GlassDensityResponse(
                            setting!.Processo,
                            setting.DensityGCm3,
                            setting.Version))
                        .ToArray()))
                : MapResult(result);
        });

        group.MapPut("/glass-densities/{processo}", async (
            string processo,
            UpdateGlassDensityRequest? body,
            IControloDefinicoesService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(ControloDefinicoesValidationErrors.DensityNotPositive);
            }

            var command = new UpdateGlassDensityCommand(
                processo,
                body.DensityGcm3 ?? 0,
                body.ExpectedVersion);

            return await ExecuteAsync(
                token => service.UpdateGlassDensityAsync(command, token),
                logger,
                cancellationToken,
                success: result => result is SettingsResult.GlassDensityUpdated(var savedProcesso, var density, var version)
                    ? Results.Ok(new GlassDensityResponse(savedProcesso, density, version))
                    : null);
        });

        return app;
    }

    // ---------------------------------------------------------------------------------------------

    private static async Task<IResult> ExecuteAsync(
        Func<CancellationToken, Task<SettingsResult>> operation,
        ILogger logger,
        CancellationToken cancellationToken,
        Func<SettingsResult, IResult?>? success = null)
    {
        var result = await operation(cancellationToken);

        // Status-level observability only: never payloads, never secrets.
        if (result is SettingsResult.Refused(var reason, var message, _))
        {
            logger.LogWarning("Definições operation refused ({Reason}): {Message}", reason, message);
        }

        if (success is not null && success(result) is { } mapped)
        {
            return mapped;
        }

        return MapResult(result);
    }

    private static IResult MapResult(SettingsResult result) => result switch
    {
        SettingsResult.PdfDirectoryFound(var view) => Results.Ok(new PdfDirectoryResponse(
            view is null ? null : view.BaseDirectory,
            view?.Version)),

        SettingsResult.PdfDirectorySaved(var version) => Results.Ok(new PdfDirectorySavedResponse(version)),

        SettingsResult.PdfDirectoryCheck(var check) => Results.Ok(new PdfDirectoryCheckResponse(
            CheckToken(check.State),
            check.Message)),

        SettingsResult.EmailListsFound(var lists) => Results.Ok(new EmailListsResponse(
            lists
                .Select(list => new EmailListItemResponse(
                    list.EmailListId,
                    list.Name,
                    list.RecipientCount,
                    list.Version))
                .ToArray())),

        SettingsResult.EmailListFound(var list) => Results.Ok(new EmailListResponse(
            list.EmailListId.Value,
            list.Name,
            list.Version,
            list.Recipients.Select(recipient => recipient.Address).ToArray())),

        SettingsResult.EmailListCreated(var id, var version) => Results.Created(
            $"{DefinicoesBasePath}/email-lists/{id}",
            new EmailListCreatedResponse(id, version)),

        SettingsResult.EmailListUpdated(var id, var version) => Results.Ok(
            new EmailListUpdatedResponse(id, version)),

        SettingsResult.EmailListDeleted => Results.NoContent(),

        SettingsResult.EmailTemplatesFound(var templates) => Results.Ok(new EmailTemplatesResponse(
            templates
                .Select(template => new EmailTemplateItemResponse(
                    template.EmailTemplateId.Value,
                    template.Name,
                    template.Subject,
                    template.Body,
                    template.DocumentType is { } type
                        ? EmailTemplateDocumentTypeTokens.ToToken(type)
                        : null,
                    template.MachineGroup is { } group
                        ? EmailMachineGroupTokens.ToToken(group)
                        : null,
                    template.EmailListId?.Value,
                    template.Version))
                .ToArray())),

        SettingsResult.EmailTemplateFound(var template) => Results.Ok(new EmailTemplateResponse(
            template.EmailTemplateId.Value,
            template.Name,
            template.Subject,
            template.Body,
            template.DocumentType is { } type
                ? EmailTemplateDocumentTypeTokens.ToToken(type)
                : null,
            template.MachineGroup is { } group
                ? EmailMachineGroupTokens.ToToken(group)
                : null,
            template.EmailListId?.Value,
            template.Version)),

        SettingsResult.EmailTemplateCreated(var id, var version) => Results.Created(
            $"{DefinicoesBasePath}/email-templates/{id}",
            new EmailTemplateCreatedResponse(id, version)),

        SettingsResult.EmailTemplateUpdated(var id, var version) => Results.Ok(
            new EmailTemplateUpdatedResponse(id, version)),

        SettingsResult.EmailTemplateDeleted => Results.NoContent(),

        SettingsResult.GlassDensitiesFound(var densities) => Results.Ok(new GlassDensitiesResponse(
            CanonicalProcessos
                .Select(processo => densities.FirstOrDefault(setting => setting.Processo == processo))
                .Where(setting => setting is not null)
                .Select(setting => new GlassDensityResponse(
                    setting!.Processo,
                    setting.DensityGCm3,
                    setting.Version))
                .ToArray())),

        SettingsResult.GlassDensityUpdated(var processo, var density, var version) =>
            Results.Ok(new GlassDensityResponse(processo, density, version)),

        SettingsResult.ValidationFailed(var errors) => ValidationFailed(errors),

        SettingsResult.NotFound(var id) => Results.NotFound(new { reason = "not-found", id }),

        SettingsResult.DuplicateName(var name) => Results.Conflict(new
        {
            reason = "duplicate-name",
            message = "Another configuration record already owns this name.",
            name,
        }),

        SettingsResult.Refused(var reason, var message, var dependencies) =>
            Results.Conflict(new SettingsRefusalResponse(
                RefusalToken(reason),
                message,
                dependencies?
                    .Select(dependency => new SettingsDependencyResponse(
                        dependency.Kind,
                        dependency.Description))
                    .ToArray())),

        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };

    private static IResult ValidationFailed(params string[] errors) =>
        Results.BadRequest(new { reason = "validation-failed", errors });

    private static IResult ValidationFailed(IReadOnlyList<string> errors) =>
        Results.BadRequest(new { reason = "validation-failed", errors });

    /// <summary>The exact transport token of a settings refusal (§26.2).</summary>
    public static string RefusalToken(SettingsRefusalReason reason) => reason switch
    {
        SettingsRefusalReason.StaleVersion => "stale-version",
        SettingsRefusalReason.DependencyExists => "dependency-exists",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown refusal reason."),
    };

    /// <summary>The exact typed check-state token (§12.2): never conflated states.</summary>
    public static string CheckToken(PdfDirectoryCheckState state) => state switch
    {
        PdfDirectoryCheckState.NotConfigured => "not-configured",
        PdfDirectoryCheckState.Ok => "ok",
        PdfDirectoryCheckState.DirectoryNotFound => "directory-not-found",
        PdfDirectoryCheckState.NotADirectory => "not-a-directory",
        PdfDirectoryCheckState.AccessDenied => "access-denied",
        PdfDirectoryCheckState.InvalidPath => "invalid-path",
        PdfDirectoryCheckState.CheckFailed => "check-failed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown check state."),
    };

    // ---------------------------------------------------------------------------------------------
    // Transport shapes
    // ---------------------------------------------------------------------------------------------

    /// <summary>Route 15 configure/change carrier (server-host absolute path).</summary>
    public sealed record SetPdfDirectoryRequest(string? BaseDirectory, int? ExpectedVersion);

    /// <summary>Route 15 read response (<c>configured:false</c> = the explicit not-configured state).</summary>
    public sealed record PdfDirectoryResponse(string? BaseDirectory, int? Version);

    /// <summary>Route 15 configure/change response.</summary>
    public sealed record PdfDirectorySavedResponse(int Version);

    /// <summary>Route 15 check response: the typed §12.2 vocabulary only.</summary>
    public sealed record PdfDirectoryCheckResponse(string State, string? Message);

    /// <summary>Route 16 create-list carrier.</summary>
    public sealed record CreateEmailListRequest(string? Name, IReadOnlyList<string>? Recipients);

    /// <summary>Route 16 update-list carrier (complete recipient set, replace-all).</summary>
    public sealed record UpdateEmailListRequest(
        int ExpectedVersion,
        string? Name,
        IReadOnlyList<string>? Recipients);

    /// <summary>Route 16 list response.</summary>
    public sealed record EmailListsResponse(IReadOnlyList<EmailListItemResponse> Lists);

    /// <summary>One named list of the list surface.</summary>
    public sealed record EmailListItemResponse(Guid EmailListId, string Name, int RecipientCount, int Version);

    /// <summary>Route 16 read response (list + complete recipient set).</summary>
    public sealed record EmailListResponse(Guid EmailListId, string Name, int Version, IReadOnlyList<string> Recipients);

    /// <summary>Route 16 create response.</summary>
    public sealed record EmailListCreatedResponse(Guid EmailListId, int Version);

    /// <summary>Route 16 update response.</summary>
    public sealed record EmailListUpdatedResponse(Guid EmailListId, int Version);

    /// <summary>Route 17 create-template carrier (verbatim text; optional document type and the
    /// optional P2-T08 email group routing: machine group B/C + the configured recipient list).</summary>
    public sealed record CreateEmailTemplateRequest(
        string? Name,
        string? Subject,
        string? Body,
        string? DocumentType,
        string? MachineGroup,
        Guid? EmailListId);

    /// <summary>Route 17 update-template carrier.</summary>
    public sealed record UpdateEmailTemplateRequest(
        int ExpectedVersion,
        string? Name,
        string? Subject,
        string? Body,
        string? DocumentType,
        string? MachineGroup,
        Guid? EmailListId);

    /// <summary>Route 17 list response.</summary>
    public sealed record EmailTemplatesResponse(IReadOnlyList<EmailTemplateItemResponse> Templates);

    /// <summary>One template of the list.</summary>
    public sealed record EmailTemplateItemResponse(
        Guid EmailTemplateId,
        string Name,
        string Subject,
        string Body,
        string? DocumentType,
        string? MachineGroup,
        Guid? EmailListId,
        int Version);

    /// <summary>Route 17 read response.</summary>
    public sealed record EmailTemplateResponse(
        Guid EmailTemplateId,
        string Name,
        string Subject,
        string Body,
        string? DocumentType,
        string? MachineGroup,
        Guid? EmailListId,
        int Version);

    /// <summary>Route 17 create response.</summary>
    public sealed record EmailTemplateCreatedResponse(Guid EmailTemplateId, int Version);

    /// <summary>Route 17 update response.</summary>
    public sealed record EmailTemplateUpdatedResponse(Guid EmailTemplateId, int Version);

    /// <summary>Route 19 update-glass-density carrier (positive value, version-guarded). The
    /// transport member is exactly <c>densityGcm3</c> (correction contract §5.3 vocabulary).</summary>
    public sealed record UpdateGlassDensityRequest(decimal? DensityGcm3, int ExpectedVersion);

    /// <summary>Route 18 list response: exactly the two current operational values (NNPB, PS);
    /// the transport member is exactly <c>densityGcm3</c> (correction contract §5.3).</summary>
    public sealed record GlassDensitiesResponse(IReadOnlyList<GlassDensityResponse> GlassDensities);

    /// <summary>One current operational glass density of the surface (routes 18/19).</summary>
    public sealed record GlassDensityResponse(string Processo, decimal DensityGcm3, int Version);

    /// <summary>Typed, actionable settings refusal response.</summary>
    public sealed record SettingsRefusalResponse(
        string Reason,
        string Message,
        IReadOnlyList<SettingsDependencyResponse>? Dependencies);

    /// <summary>One reported settings dependency.</summary>
    public sealed record SettingsDependencyResponse(string Kind, string Description);
}