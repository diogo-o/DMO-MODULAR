using DMO.Application.ControloCreate;
using DMO.Application.Documents;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;

namespace DMO.UnitTests.Documents;

/// <summary>
/// Unit proofs of the manual Peso PDF email send (P2-T08 documents slice, Create-owned
/// operational work): the EXISTING generated PDF is attached (never regenerated/recalculated),
/// template/list resolution uses ONLY the configured Definições data (peso template wins, then
/// generic; a single configured list applies automatically, several require the operator's
/// selection; no recipient is ever hardcoded), the subject/body travel verbatim (no placeholder
/// syntax exists), the evidence is returned, and a transport failure NEVER alters the Peso
/// record, the decision or the document.
/// </summary>
public sealed class PesoPdfSendServiceTests
{
    private static readonly Guid PesoId = new("11111111-1111-1111-1111-111111111111");
    private static readonly string BaseDirectory = Path.Combine(
        Path.GetTempPath(), $"dmo-peso-pdf-send-unit-{Guid.NewGuid():N}");

    // ---- Template resolution -----------------------------------------------------------

    [Fact]
    public async Task Send_ResolvesThePesoTemplateAndAttachesTheExistingPdf()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var template = Template(name: "Peso NNPB", documentType: EmailTemplateDocumentType.Peso);
            var list = List("operacao", ["ops.b1@example.com", "chefe@example.com"]);
            var pdfBytes = "EXISTING-PDF-BYTES"u8.ToArray();
            var transport = new FakeEmailTransport();
            var service = CreateService(
                templates: [template], lists: [list], pdfBytes: pdfBytes, transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId, EmailListId: null),
                CancellationToken.None);

            var sent = Assert.IsType<PesoPdfSendResult.Sent>(result);
            Assert.Equal(PesoId, sent.Evidence.PesoId);
            Assert.Equal("Peso_REF-X_B1.pdf", sent.Evidence.FileName);
            Assert.Equal("Peso NNPB", sent.Evidence.TemplateName);
            Assert.Equal(
                new[] { "chefe@example.com", "ops.b1@example.com" }, // address ASC, deterministic
                sent.Evidence.Recipients);

            // The transport received the EXISTING bytes as the attachment and the template text
            // VERBATIM (no placeholder syntax exists — nothing is substituted).
            var message = Assert.Single(transport.Messages);
            Assert.Equal("Assunto do Peso", message.Subject);
            Assert.Equal("Corpo do envio do Peso.", message.Body);
            Assert.Equal("Peso_REF-X_B1.pdf", message.AttachmentFileName);
            Assert.Equal(pdfBytes, message.AttachmentBytes);
            Assert.Equal(
                new[] { "chefe@example.com", "ops.b1@example.com" },
                message.To);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Send_FallsBackToTheGenericTemplateWhenNoPesoTemplateExists()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var generic = Template(name: "Genérico", documentType: null);
            var list = List("operacao", ["a@example.com"]);
            var service = CreateService(
                templates: [generic], lists: [list], pdfBytes: [1], transport: new FakeEmailTransport());

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId, EmailListId: null),
                CancellationToken.None);

            var sent = Assert.IsType<PesoPdfSendResult.Sent>(result);
            Assert.Equal("Genérico", sent.Evidence.TemplateName);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Send_WithSeveralPesoTemplatesRefusesAmbiguousAndNeverSends()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var transport = new FakeEmailTransport();
            var service = CreateService(
                templates:
                [
                    Template(name: "Peso A", EmailTemplateDocumentType.Peso),
                    Template(name: "Peso B", EmailTemplateDocumentType.Peso),
                    Template(name: "Genérico", documentType: null),
                ],
                lists: [List("operacao", ["a@example.com"])],
                pdfBytes: [1],
                transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId, EmailListId: null),
                CancellationToken.None);

            var refused = Assert.IsType<PesoPdfSendResult.Refused>(result);
            Assert.Equal(PesoPdfSendRefusalReason.EmailTemplateAmbiguous, refused.Reason);
            Assert.Empty(transport.Messages);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Send_WithoutAnyApplicableTemplateRefusesNotConfigured()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var transport = new FakeEmailTransport();
            var service = CreateService(
                templates: [], lists: [List("operacao", ["a@example.com"])], pdfBytes: [1], transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId, EmailListId: null),
                CancellationToken.None);

            Assert.Equal(
                PesoPdfSendRefusalReason.EmailTemplateNotConfigured,
                Assert.IsType<PesoPdfSendResult.Refused>(result).Reason);
            Assert.Empty(transport.Messages);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    // ---- Recipient resolution ---------------------------------------------------------

    [Fact]
    public async Task Send_HonoursTheOperatorSelectedListAndRefusesUnknownOrEmptyLists()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var selected = List("selecionada", ["sel@example.com"]);
            var other = List("outra", ["out@example.com"]);
            var template = Template("Peso", EmailTemplateDocumentType.Peso);
            var transport = new FakeEmailTransport();

            // Explicit selection is honoured even with several lists configured.
            var service = CreateService(
                templates: [template], lists: [selected, other], pdfBytes: [1], transport: transport);
            var sent = await service.SendAsync(
                new SendPesoPdfCommand(PesoId, selected.EmailListId.Value),
                CancellationToken.None);
            Assert.Equal(
                new[] { "sel@example.com" },
                Assert.IsType<PesoPdfSendResult.Sent>(sent).Evidence.Recipients);

            // Unknown id refused.
            var unknown = await service.SendAsync(
                new SendPesoPdfCommand(PesoId, Guid.NewGuid()),
                CancellationToken.None);
            Assert.Equal(
                PesoPdfSendRefusalReason.EmailListNotFound,
                Assert.IsType<PesoPdfSendResult.Refused>(unknown).Reason);

            // Empty list refused — no address is invented.
            var emptyService = CreateService(
                templates: [template], lists: [List("vazia", [])], pdfBytes: [1], transport: transport);
            var empty = await emptyService.SendAsync(
                new SendPesoPdfCommand(PesoId, EmailListId: null),
                CancellationToken.None);
            Assert.Equal(
                PesoPdfSendRefusalReason.EmailListEmpty,
                Assert.IsType<PesoPdfSendResult.Refused>(empty).Reason);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Send_WithSeveralListsRequiresTheOperatorSelection()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var transport = new FakeEmailTransport();
            var service = CreateService(
                templates: [Template("Peso", EmailTemplateDocumentType.Peso)],
                lists: [List("A", ["a@example.com"]), List("B", ["b@example.com"])],
                pdfBytes: [1],
                transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId, EmailListId: null),
                CancellationToken.None);

            Assert.Equal(
                PesoPdfSendRefusalReason.EmailListSelectionRequired,
                Assert.IsType<PesoPdfSendResult.Refused>(result).Reason);
            Assert.Empty(transport.Messages); // never guesses among several lists
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Send_WithoutAnyConfiguredListRefusesNotConfigured()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var service = CreateService(
                templates: [Template("Peso", EmailTemplateDocumentType.Peso)],
                lists: [],
                pdfBytes: [1],
                transport: new FakeEmailTransport());

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId, EmailListId: null),
                CancellationToken.None);

            Assert.Equal(
                PesoPdfSendRefusalReason.EmailListNotConfigured,
                Assert.IsType<PesoPdfSendResult.Refused>(result).Reason);
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
            var transport = new FakeEmailTransport();
            var service = CreateService(
                templates: [Template("Peso", EmailTemplateDocumentType.Peso)],
                lists: [List("operacao", ["a@example.com"])],
                pdfBytes: null, // no file stored
                transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId, EmailListId: null),
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
            var service = CreateService(
                sheets: PesoPdfFixtures.PendingStatusSheet(),
                templates: [Template("Peso", EmailTemplateDocumentType.Peso)],
                lists: [List("operacao", ["a@example.com"])],
                pdfBytes: [1],
                transport: new FakeEmailTransport());

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId, EmailListId: null),
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
            var service = CreateService(
                sheets: PesoPdfFixtures.PendingAnchorSheet(),
                templates: [Template("Peso", EmailTemplateDocumentType.Peso)],
                lists: [List("operacao", ["a@example.com"])],
                pdfBytes: [1],
                transport: new FakeEmailTransport());

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId, EmailListId: null),
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

        // No settings row → pdf-directory-not-configured.
        var notConfigured = CreateServiceWithoutSettings(
            templates: [Template("Peso", EmailTemplateDocumentType.Peso)],
            lists: [List("operacao", ["a@example.com"])]);
        var refused = await notConfigured.SendAsync(
            new SendPesoPdfCommand(PesoId, EmailListId: null),
            CancellationToken.None);
        Assert.Equal(
            PesoPdfSendRefusalReason.PdfDirectoryNotConfigured,
            Assert.IsType<PesoPdfSendResult.Refused>(refused).Reason);

        // Configured but inaccessible base → workspace-unavailable.
        var service = new PesoPdfSendService(
            new FakePdfDirectorySettingsRepository(new PdfDirectorySettings(
                Guid.NewGuid(), missingBase, Version: 1, DateTimeOffset.UtcNow)),
            new FixedProbe { Verdict = PdfDirectoryCheckState.DirectoryNotFound },
            new FakeControloRead(PesoPdfFixtures.DecidedSheet()),
            new FakeFileStore([1]),
            new FakeEmailTemplateRepository([Template("Peso", EmailTemplateDocumentType.Peso)]),
            new FakeEmailListRepository([List("operacao", ["a@example.com"])]),
            new FakeEmailTransport());
        var workspace = await service.SendAsync(
            new SendPesoPdfCommand(PesoId, EmailListId: null),
            CancellationToken.None);
        Assert.Equal(
            PesoPdfSendRefusalReason.WorkspaceUnavailable,
            Assert.IsType<PesoPdfSendResult.Refused>(workspace).Reason);
    }

    [Fact]
    public async Task Send_WithUnsafeTraversalFactsRefusesInvalidFileName()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var sheet = PesoPdfFixtures.DecidedSheet() with
            {
                Production = new PesoProductionProjection("REF/X", "2026-001", "B1", null),
            };
            var service = CreateService(
                sheets: sheet,
                templates: [Template("Peso", EmailTemplateDocumentType.Peso)],
                lists: [List("operacao", ["a@example.com"])],
                pdfBytes: [1],
                transport: new FakeEmailTransport());

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId, EmailListId: null),
                CancellationToken.None);

            Assert.Equal(
                PesoPdfSendRefusalReason.InvalidFileName,
                Assert.IsType<PesoPdfSendResult.Refused>(result).Reason);
        }
        finally
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }

    // ---- Transport outcomes -----------------------------------------------------------

    [Fact]
    public async Task Send_TransportNotConfiguredIsATypedRefusal()
    {
        Directory.CreateDirectory(BaseDirectory);
        try
        {
            var transport = new FakeEmailTransport { ForcedState = EmailTransportState.NotConfigured };
            var service = CreateService(
                templates: [Template("Peso", EmailTemplateDocumentType.Peso)],
                lists: [List("operacao", ["a@example.com"])],
                pdfBytes: [1],
                transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId, EmailListId: null),
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
            var transport = new FakeEmailTransport { ForcedState = EmailTransportState.Failed };
            var createRead = new FakeControloRead(PesoPdfFixtures.DecidedSheet());
            var service = CreateService(
                createRead: createRead,
                templates: [Template("Peso", EmailTemplateDocumentType.Peso)],
                lists: [List("operacao", ["a@example.com"])],
                pdfBytes: [1],
                transport: transport);

            var result = await service.SendAsync(
                new SendPesoPdfCommand(PesoId, EmailListId: null),
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
                : new FakeControloRead(PesoPdfFixtures.DecidedSheet())),
            new FakeFileStore(pdfBytes),
            new FakeEmailTemplateRepository(templates ?? []),
            new FakeEmailListRepository(lists ?? []),
            transport ?? new FakeEmailTransport());

    private PesoPdfSendService CreateServiceWithoutSettings(
        IReadOnlyList<EmailTemplate>? templates = null,
        IReadOnlyList<EmailList>? lists = null) =>
        new(
            new FakePdfDirectorySettingsRepository(),
            new FixedProbe(),
            new FakeControloRead(PesoPdfFixtures.DecidedSheet()),
            new FakeFileStore([1]),
            new FakeEmailTemplateRepository(templates ?? []),
            new FakeEmailListRepository(lists ?? []),
            new FakeEmailTransport());

    private static EmailTemplate Template(string name, EmailTemplateDocumentType? documentType) => new(
        EmailTemplateId.New(),
        name,
        Subject: name == "Genérico" ? "Assunto genérico" : "Assunto do Peso",
        Body: name == "Genérico" ? "Corpo genérico." : "Corpo do envio do Peso.",
        documentType,
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