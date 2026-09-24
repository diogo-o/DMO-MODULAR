using System.Net;
using System.Text.Json;
using DMO.Application.Access;
using DMO.Application.Documents;
using DMO.Domain.Controlo;
using DMO.Domain.Tools;
using DMO.IntegrationTests.ControloApprove;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PesoId = DMO.Domain.Controlo.PesoId;

namespace DMO.IntegrationTests.ControloCreate;

/// <summary>
/// HTTP-class proofs of the automatic group-email send of the Peso PDF (P2-T08 email slice): the
/// machine resolves the group (B1/B2/B3 → B; C1/C2/C3 → C) and the group's configured
/// template/list route the EXISTING generated PDF (never regenerated); a group without template
/// is a typed informative refusal; a transport failure is a typed 409 that NEVER changes the
/// Peso record or the decision; and the route is gated by the Controlo Create policy (an
/// approve-only caller is denied). NO body is ever sent — no recipient/list selection exists.
/// </summary>
/// <remarks>
/// The transport is replaced by a recording double (the real SMTP adapter cannot send in tests);
/// everything else — renderer, file store over a real temp base directory, orchestration,
/// repositories — is the real production stack over the shared in-memory composition.</remarks>
public sealed class PesoPdfSendEndpointsTests
{
    // ----------------------------------------------------------------------------------------
    // Fixture helpers
    // ----------------------------------------------------------------------------------------

    private static async Task<Guid> SeedApprovedPesoAsync(
        P2T06TestComposition composition,
        string referenceToken,
        string machine = "B1")
    {
        var tool = composition.SeedTool(ToolType.Cm, $"ref-{referenceToken}", "01", Processo.Nnpb, machine);
        var jobOn = composition.SeedJobOnWithCmContext(
            $"ref-{referenceToken}",
            $"pn-{referenceToken}",
            machine,
            tool.ToolId.Value,
            ToolType.Cm,
            tool.Reference,
            tool.Lot);
        var cm = jobOn.Contexts.First(context => context.ContextType == ToolContextType.Cm);
        var pesoId = await composition.SeedReviewablePesoAsync(cmId: cm.ContextId);

        var peso = (await composition.Pesos.GetByIdAsync(pesoId, CancellationToken.None))!;
        await composition.Review.DecisionAsync(
            new PesoReviewDecision(
                PesoReviewDecisionId.New(),
                PesoId.From(pesoId),
                PesoReviewDecisionKind.Aprovado,
                P2T06TestHost.ActorUserId,
                DateTimeOffset.UtcNow.AddMinutes(-5),
                Reason: null,
                PesoStatus.Pendente,
                peso.Version,
                DateTimeOffset.UtcNow.AddMinutes(-5)),
            expectedPesoVersion: peso.Version,
            CancellationToken.None);

        return pesoId;
    }

    private static async Task<Guid> SeedGroupTemplateAsync(
        P2T06TestComposition composition,
        EmailMachineGroup group,
        Guid? emailListId,
        string name)
    {
        var templateId = EmailTemplateId.New();
        await composition.Pesos.CreatedAsync(
            new EmailTemplate(
                templateId,
                Name: name,
                Subject: $"Assunto do grupo {EmailMachineGroupTokens.ToToken(group)}",
                Body: $"Segue o Peso de controlo do grupo {EmailMachineGroupTokens.ToToken(group)}.",
                DocumentType: EmailTemplateDocumentType.Peso,
                MachineGroup: group,
                EmailListId: emailListId is { } list ? EmailListId.From(list) : null,
                Version: 1,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        return templateId.Value;
    }

    private static async Task<Guid> SeedEmailListAsync(
        P2T06TestComposition composition,
        string name,
        params string[] addresses)
    {
        var listId = EmailListId.New();
        var list = new EmailList(
            listId,
            name,
            Version: 1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            Recipients: []);

        var recipients = addresses
            .Select((address, index) => new EmailRecipient(
                Guid.NewGuid(),
                listId,
                address,
                DateTimeOffset.UtcNow.AddSeconds(index)))
            .ToList();

        await composition.Pesos.CreatedAsync(list, recipients, CancellationToken.None);

        return listId.Value;
    }

    private static async Task ConfigureBaseDirectoryAsync(
        P2T06TestComposition composition,
        string baseDirectory)
    {
        await composition.Pesos.SetAsync(
            new PdfDirectorySettings(
                Guid.NewGuid(),
                baseDirectory,
                Version: 1,
                DateTimeOffset.UtcNow),
            CancellationToken.None);
    }

    private static async Task<HttpResponseMessage> GenerateAsync(HttpClient client, Guid pesoId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/controlo/create/pesos/{pesoId}/peso-pdf");

        return await P2T06TestHost.SendAuthenticatedAsync(client, request);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, Guid pesoId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/controlo/create/pesos/{pesoId}/peso-pdf/send");

        return await P2T06TestHost.SendAuthenticatedAsync(client, request);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>A real temp directory that removes itself on dispose (best-effort).</summary>
    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            FullPath = Path.Combine(
                Path.GetTempPath(),
                $"dmo-peso-pdf-send-http-{Guid.NewGuid():N}");
            Directory.CreateDirectory(FullPath);
        }

        public string FullPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(FullPath))
                {
                    Directory.Delete(FullPath, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup only.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup only.
            }
        }
    }

    /// <summary>The recording transport double (real SMTP cannot send inside tests).</summary>
    private sealed class RecordingEmailTransport : IEmailTransport
    {
        public List<EmailMessage> Messages { get; } = [];

        public EmailTransportState ForcedState { get; set; } = EmailTransportState.Sent;

        public Task<EmailTransportResult> SendAsync(
            EmailMessage message,
            CancellationToken cancellationToken)
        {
            Messages.Add(message);

            return Task.FromResult(ForcedState switch
            {
                EmailTransportState.Sent => new EmailTransportResult(EmailTransportState.Sent, null),
                EmailTransportState.NotConfigured => new EmailTransportResult(
                    EmailTransportState.NotConfigured, "Transporte não configurado (teste)."),
                _ => new EmailTransportResult(
                    EmailTransportState.Failed, "Falha de transporte (teste)."),
            });
        }
    }

    // ----------------------------------------------------------------------------------------
    // Group routing (B and C) over the real stack
    // ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Send_GroupBRoutesTheExistingPdfToTheGroupTemplateAndItsList()
    {
        using var directory = new TempDirectory();
        var composition = new P2T06TestComposition();
        var pesoId = await SeedApprovedPesoAsync(composition, "SEND1", machine: "B1");
        await ConfigureBaseDirectoryAsync(composition, directory.FullPath);
        var listId = await SeedEmailListAsync(
            composition, "Emails B", "ops.b1@example.com", "chefe.b1@example.com");
        await SeedGroupTemplateAsync(
            composition, EmailMachineGroup.B, listId, "Template B");
        var transport = new RecordingEmailTransport();

        using var factory = P2T06TestHost.ForUser(
            P2T06TestHost.CreateOnly(),
            composition,
            services =>
            {
                services.RemoveAll<IEmailTransport>();
                services.AddSingleton<IEmailTransport>(transport);
            });
        using var client = factory.CreateClient();

        // Generate the PDF first (the send reuses the EXISTING document, never regenerates).
        using (var generated = await GenerateAsync(client, pesoId))
        {
            Assert.Equal(HttpStatusCode.OK, generated.StatusCode);
            Assert.Equal("generated", (await ReadJsonAsync(generated)).GetProperty("status").GetString());
        }

        var target = Path.Combine(
            directory.FullPath, "ref-SEND1", "pn-SEND1", "Peso_ref-SEND1_B1.pdf");
        var storedPdf = File.ReadAllBytes(target);

        // Manual send with NO body: the machine B1 resolves group B automatically.
        using (var sent = await SendAsync(client, pesoId))
        {
            Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
            var payload = await ReadJsonAsync(sent);

            Assert.Equal("sent", payload.GetProperty("status").GetString());
            Assert.Equal(pesoId, payload.GetProperty("pesoId").GetGuid());
            Assert.Equal("Peso_ref-SEND1_B1.pdf", payload.GetProperty("fileName").GetString());
            Assert.Equal("Template B", payload.GetProperty("templateName").GetString());
            Assert.Equal("B", payload.GetProperty("machineGroup").GetString());
            Assert.Equal(2, payload.GetProperty("recipients").GetArrayLength());
        }

        // The transport received the EXISTING bytes and the group's configured recipients
        // (deterministic order) — no hardcoded address anywhere.
        var message = Assert.Single(transport.Messages);
        Assert.Equal(new[] { "chefe.b1@example.com", "ops.b1@example.com" }, message.To);
        Assert.Equal("Peso_ref-SEND1_B1.pdf", message.AttachmentFileName);
        Assert.Equal(storedPdf, message.AttachmentBytes);

        // No file was rewritten by the send (the existing document is reused untouched).
        Assert.Equal(storedPdf, File.ReadAllBytes(target));
    }

    [Fact]
    public async Task Send_GroupCRoutesTheExistingPdfToTheGroupCTemplateAndItsList()
    {
        using var directory = new TempDirectory();
        var composition = new P2T06TestComposition();
        var pesoId = await SeedApprovedPesoAsync(composition, "SENDC", machine: "C2");
        await ConfigureBaseDirectoryAsync(composition, directory.FullPath);
        var listId = await SeedEmailListAsync(composition, "Emails C", "ops.c@example.com");
        await SeedGroupTemplateAsync(
            composition, EmailMachineGroup.C, listId, "Template C");
        var transport = new RecordingEmailTransport();

        using var factory = P2T06TestHost.ForUser(
            P2T06TestHost.CreateOnly(),
            composition,
            services =>
            {
                services.RemoveAll<IEmailTransport>();
                services.AddSingleton<IEmailTransport>(transport);
            });
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await GenerateAsync(client, pesoId)).StatusCode);

        using var sent = await SendAsync(client, pesoId);

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var payload = await ReadJsonAsync(sent);
        Assert.Equal("C", payload.GetProperty("machineGroup").GetString());
        Assert.Equal("Template C", payload.GetProperty("templateName").GetString());
        Assert.Equal(new[] { "ops.c@example.com" }, Assert.Single(transport.Messages).To);
    }

    // ----------------------------------------------------------------------------------------
    // Typed refusals of the routing
    // ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Send_GroupWithoutATemplateRefusesEmailGroupNotConfigured()
    {
        using var directory = new TempDirectory();
        var composition = new P2T06TestComposition();
        var pesoId = await SeedApprovedPesoAsync(composition, "SEND2", machine: "B2");
        await ConfigureBaseDirectoryAsync(composition, directory.FullPath);
        await SeedEmailListAsync(composition, "Emails B", "ops@example.com");
        // NO B-group template configured.
        var transport = new RecordingEmailTransport();

        using var factory = P2T06TestHost.ForUser(
            P2T06TestHost.CreateOnly(),
            composition,
            services =>
            {
                services.RemoveAll<IEmailTransport>();
                services.AddSingleton<IEmailTransport>(transport);
            });
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await GenerateAsync(client, pesoId)).StatusCode);

        using var response = await SendAsync(client, pesoId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "email-group-not-configured",
            (await ReadJsonAsync(response)).GetProperty("reason").GetString());
        Assert.Empty(transport.Messages);
    }

    [Fact]
    public async Task Send_WithoutAnExistingPdfRefusesPdfNotGeneratedAndNeverRegenerates()
    {
        using var directory = new TempDirectory();
        var composition = new P2T06TestComposition();
        var pesoId = await SeedApprovedPesoAsync(composition, "SEND3", machine: "B1");
        await ConfigureBaseDirectoryAsync(composition, directory.FullPath);
        var listId = await SeedEmailListAsync(composition, "Emails B", "ops@example.com");
        await SeedGroupTemplateAsync(composition, EmailMachineGroup.B, listId, "Template B");
        var transport = new RecordingEmailTransport();

        using var factory = P2T06TestHost.ForUser(
            P2T06TestHost.CreateOnly(),
            composition,
            services =>
            {
                services.RemoveAll<IEmailTransport>();
                services.AddSingleton<IEmailTransport>(transport);
            });
        using var client = factory.CreateClient();

        // NO generation was performed: the send refuses instead of regenerating/recalculating.
        using var response = await SendAsync(client, pesoId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "pdf-not-generated",
            (await ReadJsonAsync(response)).GetProperty("reason").GetString());
        Assert.Empty(transport.Messages);
        Assert.False(Directory.Exists(Path.Combine(directory.FullPath, "ref-SEND3")));
    }

    // ----------------------------------------------------------------------------------------
    // Failure semantics: never a record/decision change
    // ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Send_TransportFailureIsTypedAndNeverChangesTheApprovalOrThePeso()
    {
        using var directory = new TempDirectory();
        var composition = new P2T06TestComposition();
        var pesoId = await SeedApprovedPesoAsync(composition, "SEND5", machine: "B3");
        await ConfigureBaseDirectoryAsync(composition, directory.FullPath);
        var listId = await SeedEmailListAsync(composition, "Emails B", "ops@example.com");
        await SeedGroupTemplateAsync(composition, EmailMachineGroup.B, listId, "Template B");
        var transport = new RecordingEmailTransport { ForcedState = EmailTransportState.Failed };

        using var factory = P2T06TestHost.ForUser(
            P2T06TestHost.CreateOnly(),
            composition,
            services =>
            {
                services.RemoveAll<IEmailTransport>();
                services.AddSingleton<IEmailTransport>(transport);
            });
        using var client = factory.CreateClient();
        var pesoBefore = (await composition.Pesos.GetByIdAsync(pesoId, CancellationToken.None))!;

        Assert.Equal(HttpStatusCode.OK, (await GenerateAsync(client, pesoId)).StatusCode);

        using var response = await SendAsync(client, pesoId);

        // The typed refusal — NOT a 500, NOT a fake success.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "email-send-failed",
            (await ReadJsonAsync(response)).GetProperty("reason").GetString());

        // The Peso record and the approval are untouched: same status, version and attribution
        // (a failed email never alters the approval nor the peso_id).
        var pesoAfter = (await composition.Pesos.GetByIdAsync(pesoId, CancellationToken.None))!;
        Assert.Equal(PesoStatus.Aprovado, pesoAfter.Status);
        Assert.Equal(pesoBefore.Version, pesoAfter.Version);
        Assert.Equal(pesoBefore.SubmittedByUserId, pesoAfter.SubmittedByUserId);
        Assert.Equal(pesoBefore.SubmittedAt, pesoAfter.SubmittedAt);
    }

    // ----------------------------------------------------------------------------------------
    // Access (the owning workflow gate)
    // ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Send_IsDeniedToAnApproveOnlyCaller()
    {
        using var directory = new TempDirectory();
        var composition = new P2T06TestComposition();
        var pesoId = await SeedApprovedPesoAsync(composition, "SEND6", machine: "B1");
        await ConfigureBaseDirectoryAsync(composition, directory.FullPath);
        var listId = await SeedEmailListAsync(composition, "Emails B", "ops@example.com");
        await SeedGroupTemplateAsync(composition, EmailMachineGroup.B, listId, "Template B");

        // Approve-only grant: the decision surface owns no operational document work.
        using var factory = P2T06TestHost.ForUser(P2T06TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, pesoId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}