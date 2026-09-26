using DMO.Application.Controlo.Pesos;
using DMO.Application.Controlo.Settings;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;

namespace DMO.UnitTests.Controlo.Settings;

/// <summary>
/// P2-T05 unit proofs of the Definições validation: rows SET6, SET7, SET9, SET1 and the
/// glass-density correction rows GD-V1/GD-V2 of the test-to-acceptance matrix
/// (<c>plans/contracts/P2-T05_CONTROLO_CREATE_CONTRACT.md</c> §26.4), over the closed
/// §12.2/§13.2/§14.2 token set.
/// </summary>
/// <remarks>
/// The pure static validator runs before any write and returns the exact contracted codes. The one
/// DB-backed referee of SET6 — the same address refused twice in one list — lives on the
/// <c>UNIQUE (email_list_id, address)</c> constraint of the persistence layer, so it is proved here
/// through the real service path that maps the typed persistence failure onto
/// <c>ADDRESS_INVALID</c> (the validator itself only checks the minimal address shape; the contract
/// row SET6 is class DB for exactly this reason).
/// <para>
/// <b>Superseded (F-06 cleanup, Owner clarification P2-T07 §34.3 / P2-T05 §31.3):</b> the repairer
/// validation proofs (REP1/REP2, MAC1) exercised the residual Controlo repairer surface, which is
/// DEAD in production and was removed; the live repairer validation is owned by
/// <c>BoquilhasDefinicoesService.BoquilhasDefinicoesValidator</c> and covered by the Boquilhas
/// tests. The shared error tokens (<c>NAME_REQUIRED</c>, <c>REPAIRER_NOT_FOUND</c>,
/// <c>MACHINE_UNKNOWN</c>) remain in <see cref="ControloDefinicoesValidationErrors"/> because the
/// Boquilhas surface consumes them.</para>
/// </remarks>
public sealed class ControloDefinicoesValidatorTests
{
    /// <summary>
    /// SET6 (AC-F4) — recipient addresses follow the minimal unbroken shape: exactly one <c>@</c>,
    /// non-blank local part and domain, no whitespace. Broken shapes are refused with exactly
    /// <c>ADDRESS_INVALID</c>, blanks with <c>ADDRESS_REQUIRED</c>; the same address is legal across
    /// two separate lists, and an address repeated inside ONE list is refused by the
    /// <c>(email_list_id, address)</c> uniqueness backstop mapped by the service onto the same
    /// <c>ADDRESS_INVALID</c> token.
    /// </summary>
    [Fact]
    public async Task SET6_AddressShapesUseTheMinimalUnbrokenRuleAndTheExactTokens()
    {
        // The shape predicate: the accepted form plus every broken form.
        Assert.True(ControloDefinicoesValidator.IsMinimalAddressShape("a@b.com"));
        Assert.False(ControloDefinicoesValidator.IsMinimalAddressShape("no-at-sign"));
        Assert.False(ControloDefinicoesValidator.IsMinimalAddressShape("a@b@c"));
        Assert.False(ControloDefinicoesValidator.IsMinimalAddressShape("a b@c"));
        Assert.False(ControloDefinicoesValidator.IsMinimalAddressShape("@b"));
        Assert.False(ControloDefinicoesValidator.IsMinimalAddressShape("a@"));
        Assert.False(ControloDefinicoesValidator.IsMinimalAddressShape(""));
        Assert.False(ControloDefinicoesValidator.IsMinimalAddressShape("  "));

        // The create-list validation surfaces the exact tokens.
        Assert.Empty(ControloDefinicoesValidator.Validate(
            new CreateEmailListCommand("Lista", ["a@b.com"])));

        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.AddressInvalid },
            ControloDefinicoesValidator.Validate(new CreateEmailListCommand("Lista", ["no-at-sign"])));
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.AddressInvalid },
            ControloDefinicoesValidator.Validate(new CreateEmailListCommand("Lista", ["a@b@c"])));
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.AddressInvalid },
            ControloDefinicoesValidator.Validate(new CreateEmailListCommand("Lista", ["a b@c"])));
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.AddressInvalid },
            ControloDefinicoesValidator.Validate(new CreateEmailListCommand("Lista", ["@b"])));
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.AddressInvalid },
            ControloDefinicoesValidator.Validate(new CreateEmailListCommand("Lista", ["a@"])));

        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.AddressRequired },
            ControloDefinicoesValidator.Validate(new CreateEmailListCommand("Lista", [""])));
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.AddressRequired },
            ControloDefinicoesValidator.Validate(new CreateEmailListCommand("Lista", ["   "])));

        // Cross-list repetition is allowed: the same address validates clean in two separate
        // commands, and the service persists both lists.
        Assert.Empty(ControloDefinicoesValidator.Validate(
            new CreateEmailListCommand("Lista A", ["a@b.com"])));
        Assert.Empty(ControloDefinicoesValidator.Validate(
            new CreateEmailListCommand("Lista B", ["a@b.com"])));

        var storingLists = new StoringEmailListRepository();
        var service = BuildSettingsService(storingLists);
        var first = Assert.IsType<SettingsResult.EmailListCreated>(
            await service.CreateEmailListAsync(new CreateEmailListCommand("Lista A", ["a@b.com"]), CancellationToken.None));
        var second = Assert.IsType<SettingsResult.EmailListCreated>(
            await service.CreateEmailListAsync(new CreateEmailListCommand("Lista B", ["a@b.com"]), CancellationToken.None));
        Assert.NotEqual(first.EmailListId, second.EmailListId);
        Assert.Equal(2, storingLists.CreatedCount);

        // One address twice in ONE list is refused by the persistence uniqueness backstop, mapped
        // by the service onto exactly ADDRESS_INVALID — never a 500 and never a silent second row.
        var duplicateService = BuildSettingsService(new DuplicateAddressEmailListRepository());
        var duplicateResult = Assert.IsType<SettingsResult.ValidationFailed>(
            await duplicateService.CreateEmailListAsync(new CreateEmailListCommand("Lista", ["a@b.com", "a@b.com"]), CancellationToken.None));
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.AddressInvalid },
            duplicateResult.Errors);
    }

    /// <summary>
    /// SET7 (AC-F5) — the email template carries name, subject, body, an optional document type
    /// and the optional P2-T08 email group routing (machine group B/C only): each blank fact is
    /// its own exact token, a fourth document type is <c>DOCUMENT_TYPE_UNKNOWN</c>, a group other
    /// than B/C is <c>MACHINE_GROUP_UNKNOWN</c>, and <c>null</c> plus the settled families/groups
    /// validate clean.
    /// </summary>
    [Fact]
    public void SET7_EmailTemplateFactsAreRequiredAndDocumentTypeAndGroupAreClosed()
    {
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.TemplateNameRequired },
            ControloDefinicoesValidator.Validate(new CreateEmailTemplateCommand("  ", "Assunto", "Corpo", null, null, null)));
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.SubjectRequired },
            ControloDefinicoesValidator.Validate(new CreateEmailTemplateCommand("Modelo", " ", "Corpo", null, null, null)));
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.BodyRequired },
            ControloDefinicoesValidator.Validate(new CreateEmailTemplateCommand("Modelo", "Assunto", "\t", null, null, null)));

        Assert.Equal(
            new[]
            {
                ControloDefinicoesValidationErrors.TemplateNameRequired,
                ControloDefinicoesValidationErrors.SubjectRequired,
                ControloDefinicoesValidationErrors.BodyRequired,
            },
            ControloDefinicoesValidator.Validate(new CreateEmailTemplateCommand("", "", "", null, null, null)));

        // The document type is one of peso/pegamentos/resumo or absent; anything else is unknown.
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.DocumentTypeUnknown },
            ControloDefinicoesValidator.Validate(new CreateEmailTemplateCommand("Modelo", "Assunto", "Corpo", "folha", null, null)));
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.DocumentTypeUnknown },
            ControloDefinicoesValidator.Validate(new CreateEmailTemplateCommand("Modelo", "Assunto", "Corpo", "x", null, null)));

        // The machine group is exactly B/C or absent; anything else is unknown (P2-T08 email
        // slice: B1/B2/B3 → B, C1/C2/C3 → C; no per-machine associations).
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.MachineGroupUnknown },
            ControloDefinicoesValidator.Validate(new CreateEmailTemplateCommand("Modelo", "Assunto", "Corpo", null, "A", null)));
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.MachineGroupUnknown },
            ControloDefinicoesValidator.Validate(new CreateEmailTemplateCommand("Modelo", "Assunto", "Corpo", null, "X", null)));

        Assert.Empty(ControloDefinicoesValidator.Validate(
            new CreateEmailTemplateCommand("Modelo", "Assunto", "Corpo", null, null, null)));
        Assert.Empty(ControloDefinicoesValidator.Validate(
            new CreateEmailTemplateCommand("Modelo", "Assunto", "Corpo", "peso", null, null)));
        Assert.Empty(ControloDefinicoesValidator.Validate(
            new CreateEmailTemplateCommand("Modelo", "Assunto", "Corpo", "pegamentos", null, null)));
        Assert.Empty(ControloDefinicoesValidator.Validate(
            new CreateEmailTemplateCommand("Modelo", "Assunto", "Corpo", "resumo", null, null)));
        Assert.Empty(ControloDefinicoesValidator.Validate(
            new CreateEmailTemplateCommand("Modelo", "Assunto", "Corpo", null, "B", null)));
        Assert.Empty(ControloDefinicoesValidator.Validate(
            new CreateEmailTemplateCommand("Modelo", "Assunto", "Corpo", null, "C", null)));
    }

    /// <summary>
    /// SET9 (AC-F7) — the list and template deletes are explicit confirmations: missing confirmation
    /// → exactly <c>DELETE_NOT_CONFIRMED</c>; confirmed deletes validate clean.
    /// </summary>
    [Fact]
    public void SET9_ListAndTemplateDeletesRequireExplicitConfirmation()
    {
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.DeleteNotConfirmed },
            ControloDefinicoesValidator.Validate(new DeleteEmailListCommand(Guid.NewGuid(), 1, DeleteConfirmed: false)));
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.DeleteNotConfirmed },
            ControloDefinicoesValidator.Validate(new DeleteEmailTemplateCommand(Guid.NewGuid(), 1, DeleteConfirmed: false)));

        Assert.Empty(ControloDefinicoesValidator.Validate(
            new DeleteEmailListCommand(Guid.NewGuid(), 1, DeleteConfirmed: true)));
        Assert.Empty(ControloDefinicoesValidator.Validate(
            new DeleteEmailTemplateCommand(Guid.NewGuid(), 1, DeleteConfirmed: true)));
    }

    /// <summary>
    /// SET1 (AC-F1) — the PDF base directory is an absolute server-host path: a blank value is
    /// <c>DIRECTORY_REQUIRED</c>, a relative value is <c>DIRECTORY_INVALID</c>, and a rooted value
    /// validates clean (only absolute paths reach the accessibility probe).
    /// </summary>
    [Fact]
    public void SET1_PdfDirectoryRequiresAnAbsoluteBaseDirectoryWithTheExactTokens()
    {
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.DirectoryRequired },
            ControloDefinicoesValidator.Validate(new SetPdfDirectoryCommand("", 1)));
        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.DirectoryRequired },
            ControloDefinicoesValidator.Validate(new SetPdfDirectoryCommand("   ", 1)));

        Assert.Equal(
            new[] { ControloDefinicoesValidationErrors.DirectoryInvalid },
            ControloDefinicoesValidator.Validate(new SetPdfDirectoryCommand("relativa/pasta", 1)));

        // A rooted value is accepted on any host (the check itself is the probe's job).
        var rooted = Path.Combine(Path.GetTempPath(), "dmo-pdf-settings-probe");
        Assert.True(Path.IsPathRooted(rooted));
        Assert.Empty(ControloDefinicoesValidator.Validate(new SetPdfDirectoryCommand(rooted, 1)));
    }

    /// <summary>
    /// GD-V1 (correction contract §5.3) — the glass-density update accepts exactly the canonical
    /// processo tokens: <c>NNPB</c> and <c>PS</c> validate clean; <c>npnb</c> (case), <c>TOOL</c>,
    /// a blank and any other token are <c>PROCESSO_UNKNOWN</c>; there is no alias and no other
    /// processo.
    /// </summary>
    [Fact]
    public void GD_V1_GlassDensityUpdatesAcceptOnlyTheCanonicalProcessoTokens()
    {
        Assert.Empty(ControloDefinicoesValidator.Validate(
            new UpdateGlassDensityCommand("NNPB", 2.4027m, 1)));
        Assert.Empty(ControloDefinicoesValidator.Validate(
            new UpdateGlassDensityCommand("PS", 2.4231m, 1)));

        foreach (var rejected in new[] { "npnb", "Nnpb", "PSP", "TOOL", "", "   " })
        {
            Assert.Equal(
                new[] { ControloDefinicoesValidationErrors.ProcessoUnknown },
                ControloDefinicoesValidator.Validate(
                    new UpdateGlassDensityCommand(rejected, 2.5m, 1)));
        }
    }

    /// <summary>
    /// GD-V2 (correction contract §5.3/R7) — the density must be strictly positive: zero and
    /// negative values are <c>DENSITY_NOT_POSITIVE</c>; a positive value validates clean.
    /// </summary>
    [Fact]
    public void GD_V2_GlassDensityUpdatesRequireAStrictlyPositiveDensity()
    {
        foreach (var rejected in new[] { 0m, -1m, -2.4027m })
        {
            Assert.Equal(
                new[] { ControloDefinicoesValidationErrors.DensityNotPositive },
                ControloDefinicoesValidator.Validate(
                    new UpdateGlassDensityCommand("NNPB", rejected, 1)));
        }

        Assert.Empty(ControloDefinicoesValidator.Validate(
            new UpdateGlassDensityCommand("NNPB", 0.0001m, 1)));
    }

    // ---------------------------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// SET7b (P2-T08 email slice) — the template group routing travels COMPLETE or not at all and
    /// references a real configured list: a group without its list (or a list without a group) is
    /// <c>EMAIL_LIST_NOT_FOUND</c>, an unknown list id is <c>EMAIL_LIST_NOT_FOUND</c>, and a
    /// complete routing (existing list + group) creates the template with both members.
    /// </summary>
    [Fact]
    public async Task SET7b_TemplateGroupRoutingRequiresTheGroupAndAnExistingListTogether()
    {
        var lists = new StoringEmailListRepository();
        var service = BuildSettingsService(lists);

        // Create a real list first (the routing must reference an EXISTING configured list).
        var listCreated = Assert.IsType<SettingsResult.EmailListCreated>(await service.CreateEmailListAsync(
            new CreateEmailListCommand("Emails B", ["a@example.com"]), CancellationToken.None));
        Assert.Equal("a@example.com", (await lists.ListAsync(CancellationToken.None))[0].Recipients[0].Address);

        // A group without its list: incomplete routing → refused, nothing written.
        var groupOnly = await service.CreateEmailTemplateAsync(
            new CreateEmailTemplateCommand("Tpl B", "s", "b", "peso", "B", null), CancellationToken.None);
        Assert.Contains(
            ControloDefinicoesValidationErrors.EmailListNotFound,
            Assert.IsType<SettingsResult.ValidationFailed>(groupOnly).Errors);

        // A list without its group: incomplete routing → refused.
        var listOnly = await service.CreateEmailTemplateAsync(
            new CreateEmailTemplateCommand("Tpl B", "s", "b", "peso", null, listCreated.EmailListId), CancellationToken.None);
        Assert.Contains(
            ControloDefinicoesValidationErrors.EmailListNotFound,
            Assert.IsType<SettingsResult.ValidationFailed>(listOnly).Errors);

        // An unknown list id → refused (the list must EXIST).
        var unknownList = await service.CreateEmailTemplateAsync(
            new CreateEmailTemplateCommand("Tpl B", "s", "b", "peso", "B", Guid.NewGuid()), CancellationToken.None);
        Assert.Contains(
            ControloDefinicoesValidationErrors.EmailListNotFound,
            Assert.IsType<SettingsResult.ValidationFailed>(unknownList).Errors);

        // Complete routing: created with BOTH members (group + list).
        var created = Assert.IsType<SettingsResult.EmailTemplateCreated>(await service.CreateEmailTemplateAsync(
            new CreateEmailTemplateCommand("Tpl B", "s", "b", "peso", "B", listCreated.EmailListId),
            CancellationToken.None));
        Assert.Equal(1, created.Version);

        // A template with neither group nor list stays a plain generic template (valid).
        var plain = await service.CreateEmailTemplateAsync(
            new CreateEmailTemplateCommand("Tpl Genérico", "s", "b", null, null, null), CancellationToken.None);
        Assert.IsType<SettingsResult.EmailTemplateCreated>(plain);
    }

    private static ControloDefinicoesService BuildSettingsService(IEmailListRepository emailLists) =>
        new(
            new EmptyPdfDirectoryRepository(),
            emailLists,
            new EmptyEmailTemplateRepository(),
            new EmptyGlassDensityRepository(),
            new OkDirectoryProbe());

    /// <summary>A PDF-directory settings repository that is never consulted by these proofs.</summary>
    private sealed class EmptyPdfDirectoryRepository : IPdfDirectorySettingsRepository
    {
        public Task<PdfDirectorySettings?> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult<PdfDirectorySettings?>(null);

        public Task<PdfDirectorySettings> SetAsync(
            PdfDirectorySettings settings,
            CancellationToken cancellationToken) =>
            Task.FromResult(settings);
    }

    /// <summary>A glass-density repository that is never consulted by these proofs.</summary>
    private sealed class EmptyGlassDensityRepository : IGlassDensitySettingsRepository
    {
        public Task<GlassDensitySetting?> GetByProcessoAsync(string processo, CancellationToken cancellationToken) =>
            Task.FromResult<GlassDensitySetting?>(null);

        public Task<IReadOnlyList<GlassDensitySetting>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GlassDensitySetting>>([]);

        public Task<GlassDensitySetting> UpdatedAsync(
            GlassDensitySetting setting,
            CancellationToken cancellationToken) =>
            Task.FromResult(setting);
    }

    /// <summary>An email-template repository that is never consulted by these proofs.</summary>
    private sealed class EmptyEmailTemplateRepository : IEmailTemplateRepository
    {
        public Task<EmailTemplate?> GetByIdAsync(Guid emailTemplateId, CancellationToken cancellationToken) =>
            Task.FromResult<EmailTemplate?>(null);

        public Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EmailTemplate>>([]);

        public Task<EmailTemplate> CreatedAsync(EmailTemplate template, CancellationToken cancellationToken) =>
            Task.FromResult(template);

        public Task<EmailTemplate> UpdatedAsync(EmailTemplate template, CancellationToken cancellationToken) =>
            Task.FromResult(template);

        public Task DeletedAsync(Guid emailTemplateId, int expectedVersion, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>An email-list repository that records how many lists were created.</summary>
    private sealed class StoringEmailListRepository : IEmailListRepository
    {
        private readonly List<EmailList> _lists = new();

        public int CreatedCount => _lists.Count;

        public Task<EmailList?> GetByIdAsync(Guid emailListId, CancellationToken cancellationToken) =>
            Task.FromResult(_lists.FirstOrDefault(list => list.EmailListId.Value == emailListId));

        public Task<IReadOnlyList<EmailList>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EmailList>>(_lists.ToList());

        public Task<EmailList> CreatedAsync(
            EmailList list,
            IReadOnlyList<EmailRecipient> recipients,
            CancellationToken cancellationToken)
        {
            var stored = list with { Recipients = recipients.ToList() };
            _lists.Add(stored);
            return Task.FromResult(stored);
        }

        public Task<EmailList> UpdatedAsync(
            EmailList list,
            IReadOnlyList<EmailRecipient> recipients,
            CancellationToken cancellationToken) =>
            Task.FromResult(list);

        public Task DeletedAsync(Guid emailListId, int expectedVersion, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>An email-list repository replaying the <c>UNIQUE (email_list_id, address)</c>
    /// persistence refusal of a duplicated address inside one list.</summary>
    private sealed class DuplicateAddressEmailListRepository : IEmailListRepository
    {
        public Task<EmailList?> GetByIdAsync(Guid emailListId, CancellationToken cancellationToken) =>
            Task.FromResult<EmailList?>(null);

        public Task<IReadOnlyList<EmailList>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EmailList>>([]);

        public Task<EmailList> CreatedAsync(
            EmailList list,
            IReadOnlyList<EmailRecipient> recipients,
            CancellationToken cancellationToken) =>
            throw new ControloPersistenceException(
                ControloPersistenceFailureReason.DuplicateAddress,
                "An address appears twice in one list.");

        public Task<EmailList> UpdatedAsync(
            EmailList list,
            IReadOnlyList<EmailRecipient> recipients,
            CancellationToken cancellationToken) =>
            throw new ControloPersistenceException(
                ControloPersistenceFailureReason.DuplicateAddress,
                "An address appears twice in one list.");

        public Task DeletedAsync(Guid emailListId, int expectedVersion, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>A directory probe that never claims a failure (never consulted by these proofs).</summary>
    private sealed class OkDirectoryProbe : IPdfDirectoryProbe
    {
        public PdfDirectoryCheckState Probe(string absoluteDirectoryPath) => PdfDirectoryCheckState.Ok;
    }
}