using DMO.Application.ControloCreate;
using DMO.Application.Documents;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;

namespace DMO.UnitTests.Documents;

/// <summary>
/// Unit proofs of the automatic group-email send of the Peso PDF (P2-T08 email slice): the
/// machine resolves the group EXACTLY (B1/B2/B3 → B; C1/C2/C3 → C; anything else FAILS CLOSED
/// without guessing), the group's single configured template supplies the verbatim subject/body
/// and its associated list supplies the recipients (never hardcoded), the EXISTING generated PDF
/// is attached (never regenerated/recalculated), the evidence is returned and a transport
/// failure NEVER alters the Peso record, the decision or the document.
/// </summary>
public sealed class PesoPdfSendServiceTests
{
    private static readonly Guid PesoId = new("11111111-1111-1111-1111-111111111111");
    private static readonly string BaseDirectory = Path.Combine(
        Path.GetTempPath(), $"dmo-peso-pdf-send-unit-{Guid.NewGuid():N}");

    // ---- Machine → group → template → recipients --------------------------------------

    [Theory]
    [InlineData("B1")]
    [InlineData("B2")]
    [InlineData("B3")]
    public async Task Send_BMachinesResolveTheBGroupTemplateAndItsRecipients(string machine)
    {
        await AssertGroupSendAsync(machine, "B");
    }

    [Theory]
    [InlineData("C1")]
    [InlineData("C2")]
    [InlineData("C3")]
    public async Task Send_CMachinesResolveTheCGroupTemplateAndItsRecipients(string machine)
    {
        await AssertGroupSendAsync(machine, "C");
    }

    private async Task AssertGroupSendAsync(string machine, string groupToken)
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var list = List($"emails-{groupToken}", [$"ops.{groupToken.ToLowerInvariant()}@example.com", $"chefe.{groupToken.ToLowerInvariant()}@example.com"]);
            var template = Template(
                $"Template {groupToken}",
                machineGroup: groupToken == "B" ? EmailMachineGroup.B : EmailMachineGroup.C,
                emailListId: list.EmailListId);
            var pdfBytes = "EXISTING-PDF-BYTES"u8.ToArray();
            var transport = new FakeEmailTransport();
            var service = CreateService(
                machine: machine,
                templates: [template],
                lists: [list],
                pdfBytes: pdfBytes,
                transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId),
                CancellationToken.None);

            var sent = Assert.IsType<PesoPdfSendResult.Sent>(result);
            Assert.Equal(PesoId, sent.Evidence.PesoId);
            Assert.Equal($"Peso_REF-X_{machine}.pdf", sent.Evidence.FileName);
            Assert.Equal($"Template {groupToken}", sent.Evidence.TemplateName);
            Assert.Equal(groupToken, sent.Evidence.MachineGroup);
            Assert.Equal(
                new[] { $"chefe.{groupToken.ToLowerInvariant()}@example.com", $"ops.{groupToken.ToLowerInvariant()}@example.com" }, // address ASC
                sent.Evidence.Recipients);

            // The transport received the EXISTING bytes as the attachment and the template text
            // VERBATIM (no placeholder syntax exists — nothing is substituted).
            var message = Assert.Single(transport.Messages);
            Assert.Equal($"Assunto do grupo {groupToken}", message.Subject);
            Assert.Equal($"Corpo do envio do grupo {groupToken}.", message.Body);
            Assert.Equal($"Peso_REF-X_{machine}.pdf", message.AttachmentFileName);
            Assert.Equal(pdfBytes, message.AttachmentBytes);
            Assert.Equal(sent.Evidence.Recipients, message.To);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Send_WithNoTemplateOfTheGroupRefusesEmailGroupNotConfigured()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var transport = new FakeEmailTransport();
            var service = CreateService(
                machine: "B1",
                templates: [], // no B-group template
                lists: [List("emails-b", ["a@example.com"])],
                pdfBytes: [1],
                transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId),
                CancellationToken.None);

            var refused = Assert.IsType<PesoPdfSendResult.Refused>(result);
            Assert.Equal(PesoPdfSendRefusalReason.EmailGroupNotConfigured, refused.Reason);
            Assert.Contains("grupo B", refused.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(transport.Messages);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Send_WithATemplateWithoutItsRecipientListRefusesEmailGroupNotConfigured()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var transport = new FakeEmailTransport();
            var service = CreateService(
                machine: "C2",
                templates: [Template("Template C", EmailMachineGroup.C, emailListId: null)],
                lists: [],
                pdfBytes: [1],
                transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId),
                CancellationToken.None);

            Assert.Equal(
                PesoPdfSendRefusalReason.EmailGroupNotConfigured,
                Assert.IsType<PesoPdfSendResult.Refused>(result).Reason);
            Assert.Empty(transport.Messages);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Send_WithSeveralTemplatesOfTheGroupRefusesAmbiguous()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var list = List("emails-b", ["a@example.com"]);
            var transport = new FakeEmailTransport();
            var service = CreateService(
                machine: "B2",
                templates:
                [
                    Template("Template B A", EmailMachineGroup.B, list.EmailListId),
                    Template("Template B B", EmailMachineGroup.B, list.EmailListId),
                ],
                lists: [list],
                pdfBytes: [1],
                transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId),
                CancellationToken.None);

            Assert.Equal(
                PesoPdfSendRefusalReason.EmailTemplateAmbiguous,
                Assert.IsType<PesoPdfSendResult.Refused>(result).Reason);
            Assert.Empty(transport.Messages);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Send_WithAnEmptyRecipientListRefusesEmailListEmpty()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var list = List("emails-c", []);
            var transport = new FakeEmailTransport();
            var service = CreateService(
                machine: "C1",
                templates: [Template("Template C", EmailMachineGroup.C, list.EmailListId)],
                lists: [list],
                pdfBytes: [1],
                transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId),
                CancellationToken.None);

            Assert.Equal(
                PesoPdfSendRefusalReason.EmailListEmpty,
                Assert.IsType<PesoPdfSendResult.Refused>(result).Reason);
            Assert.Empty(transport.Messages);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Send_WithAListOutsideTheConfiguredSetRefusesEmailListNotFound()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var transport = new FakeEmailTransport();
            var service = CreateService(
                machine: "B3",
                templates: [Template("Template B", EmailMachineGroup.B, EmailListId.New())], // list not in the store
                lists: [],
                pdfBytes: [1],
                transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId),
                CancellationToken.None);

            Assert.Equal(
                PesoPdfSendRefusalReason.EmailListNotFound,
                Assert.IsType<PesoPdfSendResult.Refused>(result).Reason);
            Assert.Empty(transport.Messages);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    // ---- Machines outside the set: fail closed ----------------------------------------

    [Theory]
    [InlineData("X1")]
    [InlineData("B0")]
    [InlineData("C4")]
    [InlineData("desconhecida")]
    public async Task Send_WithAMachineOutsideTheGroupsRefusesMachineGroupUnsupported(string machine)
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var list = List("emails-b", ["a@example.com"]);
            var transport = new FakeEmailTransport();
            var service = CreateService(
                machine: machine,
                templates: [Template("Template B", EmailMachineGroup.B, list.EmailListId)],
                lists: [list],
                pdfBytes: [1],
                transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId),
                CancellationToken.None);

            var refused = Assert.IsType<PesoPdfSendResult.Refused>(result);
            Assert.Equal(PesoPdfSendRefusalReason.MachineGroupUnsupported, refused.Reason);
            Assert.Contains(machine, refused.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(transport.Messages); // nothing is guessed
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    // ---- The existing document is reused, never regenerated ---------------------------

    [Fact]
    public async Task Send_WithNoStoredPdfRefusesPdfNotGeneratedWithoutSending()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var list = List("emails-b", ["a@example.com"]);
            var transport = new FakeEmailTransport();
            var service = CreateService(
                machine: "B1",
                templates: [Template("Template B", EmailMachineGroup.B, list.EmailListId)],
                lists: [list],
                pdfBytes: null, // no file stored
                transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId),
                CancellationToken.None);

            var refused = Assert.IsType<PesoPdfSendResult.Refused>(result);
            Assert.Equal(PesoPdfSendRefusalReason.PdfNotGenerated, refused.Reason);
            Assert.Empty(transport.Messages);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    // ---- Preconditions ----------------------------------------------------------------

    [Fact]
    public async Task Send_OnAnUndecidedPesoRefusesNotDecided()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var list = List("emails-b", ["a@example.com"]);
            var service = CreateService(
                sheets: PesoPdfFixtures.PendingStatusSheet(),
                machine: "B1",
                templates: [Template("Template B", EmailMachineGroup.B, list.EmailListId)],
                lists: [list],
                pdfBytes: [1],
                transport: new FakeEmailTransport());

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId),
                CancellationToken.None);

            Assert.Equal(
                PesoPdfSendRefusalReason.NotDecided,
                Assert.IsType<PesoPdfSendResult.Refused>(result).Reason);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Send_OnAPendingAnchorRefusesProductionBindingMissing()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var list = List("emails-b", ["a@example.com"]);
            var service = CreateService(
                sheets: PesoPdfFixtures.PendingAnchorSheet(),
                machine: "B1",
                templates: [Template("Template B", EmailMachineGroup.B, list.EmailListId)],
                lists: [list],
                pdfBytes: [1],
                transport: new FakeEmailTransport());

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId),
                CancellationToken.None);

            Assert.Equal(
                PesoPdfSendRefusalReason.ProductionBindingMissing,
                Assert.IsType<PesoPdfSendResult.Refused>(result).Reason);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Send_WithoutAConfiguredDirectoryOrWorkspaceRefusesTyped()
    {
        var missingBase = Path.Combine(Path.GetTempPath(), $"dmo-peso-pdf-send-missing-{Guid.NewGuid():N}");

        var notConfigured = CreateServiceWithoutSettings(
            machine: "B1",
            templates: [Template("Template B", EmailMachineGroup.B, EmailListId.New())],
            lists: []);
        var refused = await notConfigured.SendAsync(
            new SendPesoPdfCommand(PesoId),
            CancellationToken.None);
        Assert.Equal(
            PesoPdfSendRefusalReason.PdfDirectoryNotConfigured,
            Assert.IsType<PesoPdfSendResult.Refused>(refused).Reason);

        var service = new PesoPdfSendService(
            new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), missingBase, Version: 1, DateTimeOffset.UtcNow)),
            new FixedProbe { Verdict = PdfDirectoryCheckState.DirectoryNotFound },
            new FakeControloRead(PesoPdfFixtures.DecidedSheet()),
            new FakeFileStore([1]),
            new FakeEmailTemplateRepository([Template("Template B", EmailMachineGroup.B, EmailListId.New())]),
            new FakeEmailListRepository([]),
            new FakeEmailTransport());
        var workspace = await service.SendAsync(
            new SendPesoPdfCommand(PesoId),
            CancellationToken.None);
        Assert.Equal(
            PesoPdfSendRefusalReason.WorkspaceUnavailable,
            Assert.IsType<PesoPdfSendResult.Refused>(workspace).Reason);
    }

    // ---- Transport outcomes -----------------------------------------------------------

    [Fact]
    public async Task Send_TransportNotConfiguredIsATypedRefusal()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var list = List("emails-b", ["a@example.com"]);
            var transport = new FakeEmailTransport { ForcedState = EmailTransportState.NotConfigured };
            var service = CreateService(
                machine: "B1",
                templates: [Template("Template B", EmailMachineGroup.B, list.EmailListId)],
                lists: [list],
                pdfBytes: [1],
                transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId),
                CancellationToken.None);

            Assert.Equal(
                PesoPdfSendRefusalReason.EmailTransportNotConfigured,
                Assert.IsType<PesoPdfSendResult.Refused>(result).Reason);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Send_TransportFailureNeverAltersThePesoOrTheDecision()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var list = List("emails-b", ["a@example.com"]);
            var transport = new FakeEmailTransport { ForcedState = EmailTransportState.Failed };
            var createRead = new FakeControloRead(PesoPdfFixtures.DecidedSheet());
            var service = CreateService(
                createRead: createRead,
                machine: "B1",
                templates: [Template("Template B", EmailMachineGroup.B, list.EmailListId)],
                lists: [list],
                pdfBytes: [1],
                transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId),
                CancellationToken.None);

            var refused = Assert.IsType<PesoPdfSendResult.Refused>(result);
            Assert.Equal(PesoPdfSendRefusalReason.EmailSendFailed, refused.Reason);

            // The Peso was only READ (GetAsync): no create/update/submit/associate ever ran, the
            // version was never bumped and no decision changed (the fake throws on any write).
            Assert.True(createRead.GetCalls >= 1);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    // -----------------------------------------------------------------------------------
    // Fakes + builders
    // -----------------------------------------------------------------------------------

    private PesoPdfSendService CreateService(
        string machine = "B1",
        IReadOnlyList<EmailTemplate>? templates = null,
        IReadOnlyList<EmailList>? lists = null,
        byte[]? pdfBytes = null,
        FakeEmailTransport? transport = null,
        PesoSheetReadModel? sheets = null,
        FakeControloRead? createRead = null) =>
        new(
            new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), BaseDirectory, Version: 1, DateTimeOffset.UtcNow)),
            new FixedProbe(),
            createRead ?? (sheets is { } sheet
                ? new FakeControloRead(sheet)
                : new FakeControloRead(PesoPdfFixtures.DecidedSheet(machine: machine))),
            new FakeFileStore(pdfBytes),
            new FakeEmailTemplateRepository(templates ?? []),
            new FakeEmailListRepository(lists ?? []),
            transport ?? new FakeEmailTransport());

    private PesoPdfSendService CreateServiceWithoutSettings(
        string machine = "B1",
        IReadOnlyList<EmailTemplate>? templates = null,
        IReadOnlyList<EmailList>? lists = null) =>
        new(
            new FakePdfDirectorySettingsRepository(),
            new FixedProbe(),
            new FakeControloRead(PesoPdfFixtures.DecidedSheet(machine: machine)),
            new FakeFileStore([1]),
            new FakeEmailTemplateRepository(templates ?? []),
            new FakeEmailListRepository(lists ?? []),
            new FakeEmailTransport());

    private static EmailTemplate Template(
        string name,
        EmailMachineGroup? machineGroup = null,
        EmailListId? emailListId = null) => new(
        EmailTemplateId.New(),
        name,
        Subject: machineGroup is { } group ? $"Assunto do grupo {EmailMachineGroupTokens.ToToken(group)}" : "Assunto genérico",
        Body: machineGroup is { } bodyGroup ? $"Corpo do envio do grupo {EmailMachineGroupTokens.ToToken(bodyGroup)}." : "Corpo genérico.",
        DocumentType: EmailTemplateDocumentType.Peso,
        machineGroup,
        emailListId,
        Version: 1,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private static EmailList List(string name, params string[] addresses) => new(
        EmailListId.New(),
        name,
        Version: 1,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        addresses
            .Select(address => new EmailRecipient(
                Guid.NewGuid(),
                EmailListId.New(),
                address,
                DateTimeOffset.UtcNow))
            .ToList());

    private sealed class FixedProbe : IPdfDirectoryProbe
    {
        public PdfDirectoryCheckState Verdict { get; set; } = PdfDirectoryCheckState.Ok;

        public PdfDirectoryCheckState Probe(string absoluteDirectoryPath) => Verdict;
    }

    private sealed class FakePdfDirectorySettingsRepository : IPdfDirectorySettingsRepository
    {
        private readonly PdfDirectorySettings? _settings;

        public FakePdfDirectorySettingsRepository(PdfDirectorySettings? settings = null)
        {
            _settings = settings;
        }

        public Task<PdfDirectorySettings?> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_settings);

        public Task<PdfDirectorySettings> SetAsync(
            PdfDirectorySettings settings,
            CancellationToken cancellationToken) =>
            Task.FromResult(settings);
    }

    private sealed class FakeEmailTemplateRepository : IEmailTemplateRepository
    {
        private readonly IReadOnlyList<EmailTemplate> _templates;

        public FakeEmailTemplateRepository(IReadOnlyList<EmailTemplate> templates)
        {
            _templates = templates;
        }

        public Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_templates);

        public Task<EmailTemplate?> GetByIdAsync(Guid emailTemplateId, CancellationToken cancellationToken) =>
            Task.FromResult(_templates.FirstOrDefault(template => template.EmailTemplateId.Value == emailTemplateId));

        public Task<EmailTemplate> CreatedAsync(EmailTemplate template, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The send unit tests never create templates.");

        public Task<EmailTemplate> UpdatedAsync(EmailTemplate template, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The send unit tests never edit templates.");

        public Task DeletedAsync(Guid emailTemplateId, int expectedVersion, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The send unit tests never delete templates.");
    }

    private sealed class FakeEmailListRepository : IEmailListRepository
    {
        private readonly IReadOnlyList<EmailList> _lists;

        public FakeEmailListRepository(IReadOnlyList<EmailList> lists)
        {
            _lists = lists;
        }

        public Task<IReadOnlyList<EmailList>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_lists);

        public Task<EmailList?> GetByIdAsync(Guid emailListId, CancellationToken cancellationToken) =>
            Task.FromResult(_lists.FirstOrDefault(list => list.EmailListId.Value == emailListId));

        public Task<EmailList> CreatedAsync(
            EmailList list,
            IReadOnlyList<EmailRecipient> recipients,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The send unit tests never create lists.");

        public Task<EmailList> UpdatedAsync(
            EmailList list,
            IReadOnlyList<EmailRecipient> recipients,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The send unit tests never edit lists.");

        public Task DeletedAsync(Guid emailListId, int expectedVersion, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The send unit tests never delete lists.");
    }

    private sealed class FakeFileStore : IPesoPdfFileStore
    {
        private readonly byte[]? _bytes;

        public FakeFileStore(byte[]? bytes)
        {
            _bytes = bytes;
        }

        public Task<PesoPdfFileWriteResult> WriteAsync(
            string baseDirectory,
            string relativeDirectory,
            string fileName,
            byte[] content,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The send unit tests never write.");

        public Task<PesoPdfFileReadResult> ReadAsync(
            string baseDirectory,
            string relativeDirectory,
            string fileName,
            CancellationToken cancellationToken) =>
            Task.FromResult(_bytes is null
                ? PesoPdfFileReadResult.Missing()
                : PesoPdfFileReadResult.Found(_bytes));
    }

    internal sealed class FakeEmailTransport : IEmailTransport
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

    /// <summary>Read-only shared-read fake: the Peso is ONLY read (any write throws).</summary>
    private sealed class FakeControloRead : IControloCreateService
    {
        private readonly PesoResult _answer;

        public FakeControloRead(PesoSheetReadModel sheet) => _answer = new PesoResult.Found(sheet);

        public int GetCalls { get; private set; }

        public Task<PesoResult> GetAsync(Guid pesoId, CancellationToken cancellationToken)
        {
            GetCalls += 1;
            return Task.FromResult(_answer);
        }

        public Task<PesoResult> CalculateAsync(CalculatePesoCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The send unit tests never calculate.");

        public Task<PesoResult> CreateAsync(CreatePesoCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The send unit tests never create (the Peso must stay untouched).");

        public Task<PesoResult> UpdateAsync(UpdatePesoCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The send unit tests never edit (the Peso must stay untouched).");

        public Task<PesoResult> SubmitAsync(SubmitPesoCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The send unit tests never submit (the Peso must stay untouched).");

        public Task<PesoResult> AssociateAsync(AssociatePesoCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The send unit tests never associate (the Peso must stay untouched).");

        public Task<PesoResult> ListAssociationCandidatesAsync(Guid toolId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The send unit tests never list candidates.");
    }
}