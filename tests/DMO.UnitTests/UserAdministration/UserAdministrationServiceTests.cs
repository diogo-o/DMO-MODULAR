using System.Reflection;
using DMO.Application.Accounts;
using DMO.Application.Persistence;
using DMO.Application.Templates;
using DMO.Application.UserAdministration;
using DMO.UnitTests.Access.Fakes;
using DMO.UnitTests.UserAdministration.Fakes;
using DMO.Web.Endpoints.Administration;

namespace DMO.UnitTests.UserAdministration;

/// <summary>
/// P1-T05 service tests — the real <see cref="UserAdministrationService"/> with in-memory
/// persistence and provider fakes: invite-only creation (no password anywhere), strict
/// idempotent replay, orphan recovery, provider-first delete with the recoverable delete race,
/// coordinated email changes and the closed result set.
/// </summary>
public sealed class UserAdministrationServiceTests
{
    private static readonly Guid TemplateId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task List_ReturnsAllUsers_ActiveAndInactive_WithTemplateNames()
    {
        var templates = new FakeTemplateRepository();
        templates.Seed(new Template(TemplateId, "Manutenção", null, Version: 1));
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var service = CreateService(users, templates, provisioner);

        await SeedRowAsync(users, "2661", active: true, templateId: TemplateId);
        await SeedRowAsync(users, "1888", active: false, templateId: null);

        var items = await service.ListAsync(CancellationToken.None);

        Assert.Equal(2, items.Count);
        Assert.Contains(items, item => item.CompanyNumber == "2661" && item.Active && item.TemplateName == "Manutenção");
        Assert.Contains(items, item => item.CompanyNumber == "1888" && !item.Active && item.TemplateName is null);
    }

    [Fact]
    public async Task Get_ById_PopulatesFicha_WithTemplateName()
    {
        var templates = new FakeTemplateRepository();
        templates.Seed(new Template(TemplateId, "Manutenção", null, Version: 1));
        var users = new FakeUserRepository();
        var service = CreateService(users, templates, new FakeUserIdentityProvisioner());

        var account = await SeedRowAsync(users, "2661", active: true, templateId: TemplateId);

        var ficha = await service.GetAsync(account.AccountId, CancellationToken.None);

        Assert.NotNull(ficha);
        Assert.Equal("2661", ficha!.CompanyNumber);
        Assert.Equal("Manutenção", ficha.TemplateName);
        Assert.Equal(1, ficha.Version);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNull()
    {
        var service = CreateService(new FakeUserRepository(), new FakeTemplateRepository(), new FakeUserIdentityProvisioner());

        Assert.Null(await service.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // ------------------------------------------------------------------ CREATE (invite)

    [Fact]
    public async Task Create_InvitesProviderFirst_ThenPersistsRow_WithSubject()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.CreateAsync(Command(), CancellationToken.None);

        var created = Assert.IsType<UserAdministrationResult.Created>(result);
        Assert.Equal("joao@dmo.test", Assert.Single(provisioner.InvitedEmails));
        Assert.Equal("subject-1", users.SubjectOf(created.Account.AccountId));
        Assert.Equal("2661", created.Account.CompanyNumber);
        Assert.Equal("Reparador", created.Account.RoleLabel);
        Assert.True(created.Account.IsActive);
        Assert.Equal(1, created.Account.Version);
    }

    [Fact]
    public void CreateCommand_And_CreateTransportShape_HaveNoPasswordMember()
    {
        // The invite flow carries no password: neither the command nor the transport shape
        // may expose one (verified by member inspection so a future regression is caught).
        var commandProperties = typeof(UserAdministrationCommands.CreateUserCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name);
        Assert.DoesNotContain(commandProperties, name => name.Contains("password", StringComparison.OrdinalIgnoreCase));

        var requestProperties = typeof(UserAdministrationEndpoints.CreateUserRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name);
        Assert.DoesNotContain(requestProperties, name => name.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Create_UnknownTemplateId_ValidationFailed_NoProviderCall()
    {
        var provisioner = new FakeUserIdentityProvisioner();
        var service = CreateService(new FakeUserRepository(), new FakeTemplateRepository(), provisioner);

        var result = await service.CreateAsync(Command() with { TemplateId = Guid.NewGuid() }, CancellationToken.None);

        Assert.IsType<UserAdministrationResult.ValidationFailed>(result);
        Assert.Empty(provisioner.InvitedEmails);
    }

    [Fact]
    public async Task Create_BlankFacts_ValidationFailed_NoProviderCall()
    {
        var provisioner = new FakeUserIdentityProvisioner();
        var service = CreateService(new FakeUserRepository(), new FakeTemplateRepository(), provisioner);

        var result = await service.CreateAsync(
            Command() with { Name = " ", CompanyNumber = "", Email = "" },
            CancellationToken.None);

        var failed = Assert.IsType<UserAdministrationResult.ValidationFailed>(result);
        Assert.NotEmpty(failed.Errors);
        Assert.Empty(provisioner.InvitedEmails);
    }

    [Fact]
    public async Task Create_InvalidRedirectUrl_ValidationFailed_NoProviderCall()
    {
        var provisioner = new FakeUserIdentityProvisioner();
        var service = CreateService(new FakeUserRepository(), new FakeTemplateRepository(), provisioner);

        var result = await service.CreateAsync(
            Command() with { InviteRedirectUrl = "not-a-url" },
            CancellationToken.None);

        Assert.IsType<UserAdministrationResult.ValidationFailed>(result);
        Assert.Empty(provisioner.InvitedEmails);
    }

    [Fact]
    public async Task Create_DuplicateCompanyNumber_Conflict_NoProviderCall()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);
        // Existing row whose stable facts DIFFER from the retried command (another person on
        // the same company number): a material duplicate — never a replay.
        await SeedRowAsync(users, "2661", name: "Outra Pessoa");

        var result = await service.CreateAsync(Command(), CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.DuplicateCompanyNumber, conflict.Reason);
        Assert.Empty(provisioner.InvitedEmails);
    }

    [Fact]
    public async Task Create_ExistingRow_EqualStableFacts_IdempotentReplay_NoProviderCall()
    {
        // Correction D: a retry whose existing row provably equals the same completed logical
        // create result is an idempotent replay — never a blind conflict.
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);
        var row = await SeedRowAsync(
            users, "2661", active: true, templateId: null,
            name: "João Silva", email: "joao@dmo.test", role: "Reparador");

        // The retried command carries different casing/whitespace (no spaces: validation runs
        // before normalization); normalization makes the stable facts provably equal.
        var result = await service.CreateAsync(
            Command() with { CompanyNumber = " 2661 ", Email = "JOAO@dmo.test" },
            CancellationToken.None);

        var replay = Assert.IsType<UserAdministrationResult.IdempotentReplay>(result);
        Assert.Equal(row.AccountId, replay.Account.AccountId);
        Assert.Empty(provisioner.InvitedEmails);
    }

    [Fact]
    public async Task Create_ExistingRow_DifferentFacts_ConflictDuplicateCompanyNumber()
    {
        // Correction D: an existing company number whose facts differ materially is a
        // duplicate, never a replay.
        var users = new FakeUserRepository();
        var service = CreateService(users, new FakeTemplateRepository(), new FakeUserIdentityProvisioner());
        await SeedRowAsync(users, "2661", name: "Outra Pessoa");

        var result = await service.CreateAsync(Command(), CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.DuplicateCompanyNumber, conflict.Reason);
    }

    [Fact]
    public async Task Create_DuplicateAppEmail_Conflict_NoProviderCall()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);
        await SeedRowAsync(users, "7777", email: "joao@dmo.test");

        var result = await service.CreateAsync(Command(), CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.EmailInUse, conflict.Reason);
        Assert.Empty(provisioner.InvitedEmails);
    }

    [Fact]
    public async Task Create_ProviderInviteFailure_NoDbTouch()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner
        {
            InviteFailure = new ProviderUserOperationException(ProviderUserOperationFailure.Error, "boom"),
        };
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.CreateAsync(Command(), CancellationToken.None);

        var failed = Assert.IsType<UserAdministrationResult.ProviderFailed>(result);
        Assert.Equal(ProviderFailureKind.Error, failed.Kind);
        Assert.Empty(users.Accounts);
    }

    [Fact]
    public async Task Create_ProviderUnavailable_NoDbTouch()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner
        {
            InviteFailure = new ProviderUserOperationException(ProviderUserOperationFailure.Unavailable, "down"),
        };
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.CreateAsync(Command(), CancellationToken.None);

        var failed = Assert.IsType<UserAdministrationResult.ProviderFailed>(result);
        Assert.Equal(ProviderFailureKind.Unavailable, failed.Kind);
        Assert.Empty(users.Accounts);
    }

    [Fact]
    public async Task Create_ProviderSuccess_DbFailure_CompensatesDelete_WhenIdentityUnreferenced()
    {
        // Accepted compensation: DB create fails after the fresh invite -> delete the identity
        // provably created by this operation (no application row references it).
        var users = new FakeUserRepository
        {
            CreateFailure = new UserPersistenceException(UserPersistenceFailureReason.DuplicateCompanyNumber, "dup"),
        };
        var provisioner = new FakeUserIdentityProvisioner();
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.CreateAsync(Command(), CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.DuplicateCompanyNumber, conflict.Reason);
        Assert.Equal("subject-1", Assert.Single(provisioner.DeletedSubjects));
        Assert.Empty(users.Accounts);
    }

    [Fact]
    public async Task Create_ProviderSuccess_DbFailure_SubjectNowReferenced_NoCompensationDelete()
    {
        // Never delete a provider identity already referenced by another application USER: a
        // concurrent winner claimed the subject, so no compensation delete runs.
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        await SeedRowAsync(users, "7777", email: "outro@dmo.test", subject: "subject-1");
        users.CreateFailure = new UserPersistenceException(UserPersistenceFailureReason.DuplicateProviderSubject, "dup-subject");
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.CreateAsync(Command(), CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.EmailInUse, conflict.Reason);
        Assert.Empty(provisioner.DeletedSubjects);
    }

    [Fact]
    public async Task Create_DbFailure_CleanupFailure_ExplicitProviderFailure_NotSuccess()
    {
        var users = new FakeUserRepository
        {
            CreateFailure = new UserPersistenceException(UserPersistenceFailureReason.DuplicateCompanyNumber, "dup"),
        };
        var provisioner = new FakeUserIdentityProvisioner
        {
            DeleteFailure = new ProviderUserOperationException(ProviderUserOperationFailure.Error, "cleanup failed"),
        };
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.CreateAsync(Command(), CancellationToken.None);

        // Cleanup failure is never reported as success.
        var failed = Assert.IsType<UserAdministrationResult.ProviderFailed>(result);
        Assert.Equal(ProviderFailureKind.Error, failed.Kind);
        Assert.Single(provisioner.DeletedSubjects);
    }

    [Fact]
    public async Task Create_OrphanProviderIdentity_AdoptsSubject_RecoveredProvisioning_NoInvite()
    {
        // Accepted orphan recovery: exact carrier email match with no application row -> adopt
        // the provably orphaned provisioning instead of creating the identity again.
        var provisioner = new FakeUserIdentityProvisioner();
        provisioner.Seed(new ProviderUser("orphan-subject", "joao@dmo.test"));
        var users = new FakeUserRepository();
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.CreateAsync(Command(), CancellationToken.None);

        var recovered = Assert.IsType<UserAdministrationResult.RecoveredProvisioning>(result);
        Assert.Equal("orphan-subject", users.SubjectOf(recovered.Account.AccountId));
        Assert.Empty(provisioner.InvitedEmails);
        Assert.Empty(provisioner.DeletedSubjects);
    }

    [Fact]
    public async Task Create_OrphanProviderIdentity_AlreadyMapped_ConflictEmailInUse_NoInvite()
    {
        var provisioner = new FakeUserIdentityProvisioner();
        provisioner.Seed(new ProviderUser("mapped-subject", "joao@dmo.test"));
        var users = new FakeUserRepository();
        await SeedRowAsync(users, "7777", email: "outro@dmo.test", subject: "mapped-subject");
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.CreateAsync(Command(), CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.EmailInUse, conflict.Reason);
        Assert.Empty(provisioner.InvitedEmails);
    }

    [Fact]
    public async Task Create_ProviderInconsistency_FailsClosed()
    {
        var provisioner = new FakeUserIdentityProvisioner
        {
            FindFailure = new ProviderUserOperationException(
                ProviderUserOperationFailure.Error,
                "more than one provider identity for the carrier email"),
        };
        var users = new FakeUserRepository();
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.CreateAsync(Command(), CancellationToken.None);

        // Multiple exact provider matches are a provider inconsistency: fail closed, never
        // adopt an arbitrary identity.
        var failed = Assert.IsType<UserAdministrationResult.ProviderFailed>(result);
        Assert.Equal(ProviderFailureKind.Error, failed.Kind);
        Assert.Empty(users.Accounts);
        Assert.Empty(provisioner.InvitedEmails);
    }

    [Fact]
    public async Task Create_Invite422Race_ConflictEmailInUse()
    {
        // A confirmed identity appeared between the provider lookup and the invite: the create
        // surfaces the conflict without blind re-invites or blind adoption.
        var provisioner = new FakeUserIdentityProvisioner
        {
            InviteFailure = new ProviderUserOperationException(ProviderUserOperationFailure.EmailAlreadyInUse, "422"),
        };
        var service = CreateService(new FakeUserRepository(), new FakeTemplateRepository(), provisioner);

        var result = await service.CreateAsync(Command(), CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.EmailInUse, conflict.Reason);
    }

    [Fact]
    public async Task Create_NormalizesCompanyNumber_And_CarrierEmail_ForPersistence()
    {
        var users = new FakeUserRepository();
        var service = CreateService(users, new FakeTemplateRepository(), new FakeUserIdentityProvisioner());

        var result = await service.CreateAsync(
            Command() with { CompanyNumber = "  2661  ", Email = "JOAO@DMO.TEST" },
            CancellationToken.None);

        var created = Assert.IsType<UserAdministrationResult.Created>(result);
        Assert.Equal("2661", created.Account.CompanyNumber);
        Assert.Equal("joao@dmo.test", created.Account.Email);
    }

    [Fact]
    public async Task Create_AcceptsRoleLabel_Operador_Responsavel_Admin_FreeText()
    {
        // Role is free text, presentation only: "Operador", "Responsável" and "Admin" create
        // and grant nothing (the gate tests prove they never authorize).
        foreach (var role in new[] { "Operador", "Responsável", "Manager", "Reparador", "Admin" })
        {
            var users = new FakeUserRepository();
            var service = CreateService(users, new FakeTemplateRepository(), new FakeUserIdentityProvisioner());

            var result = await service.CreateAsync(
                Command() with { CompanyNumber = $"cn-{Guid.NewGuid():N}", Role = role },
                CancellationToken.None);

            var created = Assert.IsType<UserAdministrationResult.Created>(result);
            Assert.Equal(role, created.Account.RoleLabel);
        }
    }

    // ------------------------------------------------------------------ RESEND INVITE

    [Fact]
    public async Task ResendInvite_CallsInviteWithCarrierEmail_OnExpectedVersion()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var row = await SeedRowAsync(users, "2661", subject: "subject-1");
        provisioner.Seed(new ProviderUser("subject-1", "joao@dmo.test"));
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.ResendInviteAsync(
            new UserAdministrationCommands.ResendInviteCommand(row.AccountId, ExpectedVersion: 1),
            CancellationToken.None);

        Assert.IsType<UserAdministrationResult.Success>(result);
        Assert.Equal("joao@dmo.test", Assert.Single(provisioner.InvitedEmails));
    }

    [Fact]
    public async Task ResendInvite_StaleFicha_Conflict_NoProviderCall()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var row = await SeedRowAsync(users, "2661", subject: "subject-1", version: 2);
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.ResendInviteAsync(
            new UserAdministrationCommands.ResendInviteCommand(row.AccountId, ExpectedVersion: 1),
            CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.StaleVersion, conflict.Reason);
        Assert.Empty(provisioner.InvitedEmails);
    }

    [Fact]
    public async Task ResendInvite_ConfirmedUser_422_ValidationFailed()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var row = await SeedRowAsync(users, "2661", subject: "subject-1");
        provisioner.Seed(new ProviderUser("subject-1", "joao@dmo.test"));
        provisioner.Confirm("joao@dmo.test");
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.ResendInviteAsync(
            new UserAdministrationCommands.ResendInviteCommand(row.AccountId, ExpectedVersion: 1),
            CancellationToken.None);

        // Confirmed identity: the invite endpoint rejects; the resend surfaces the safe
        // validation state and the operator uses password reset instead.
        var failed = Assert.IsType<UserAdministrationResult.ValidationFailed>(result);
        Assert.Contains(failed.Errors, error => error.Contains("confirmed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResendInvite_MissingUser_NotFound()
    {
        var service = CreateService(new FakeUserRepository(), new FakeTemplateRepository(), new FakeUserIdentityProvisioner());

        var result = await service.ResendInviteAsync(
            new UserAdministrationCommands.ResendInviteCommand(Guid.NewGuid(), ExpectedVersion: 1),
            CancellationToken.None);

        Assert.IsType<UserAdministrationResult.NotFound>(result);
    }

    [Fact]
    public async Task ResendInvite_ProviderFailure_ProviderFailed()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner
        {
            InviteFailure = new ProviderUserOperationException(ProviderUserOperationFailure.Error, "boom"),
        };
        var row = await SeedRowAsync(users, "2661", subject: "subject-1");
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.ResendInviteAsync(
            new UserAdministrationCommands.ResendInviteCommand(row.AccountId, ExpectedVersion: 1),
            CancellationToken.None);

        var failed = Assert.IsType<UserAdministrationResult.ProviderFailed>(result);
        Assert.Equal(ProviderFailureKind.Error, failed.Kind);
    }

    // ------------------------------------------------------------------ EDIT

    [Fact]
    public async Task Update_EditsApplicationFields_NoProviderCall()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var row = await SeedRowAsync(users, "2661", subject: "subject-1");
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.UpdateAsync(
            new UserAdministrationCommands.UpdateUserCommand(
                row.AccountId, "João Silva 2", "2661", "joao@dmo.test", "Chefe", ExpectedVersion: 1),
            CancellationToken.None);

        var success = Assert.IsType<UserAdministrationResult.Success>(result);
        Assert.Equal("João Silva 2", success.Account.DisplayName);
        Assert.Equal("Chefe", success.Account.RoleLabel);
        Assert.Equal(2, success.Account.Version);
        Assert.Empty(provisioner.UpdatedEmails);
    }

    [Fact]
    public async Task Update_CompanyNumberChange_DoesNotTouchProvider()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var row = await SeedRowAsync(users, "2661", subject: "subject-1");
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.UpdateAsync(
            new UserAdministrationCommands.UpdateUserCommand(
                row.AccountId, "João Silva", "7711", "joao@dmo.test", "Reparador", ExpectedVersion: 1),
            CancellationToken.None);

        var success = Assert.IsType<UserAdministrationResult.Success>(result);
        Assert.Equal("7711", success.Account.CompanyNumber);
        // The provider is never coupled to the company number.
        Assert.Empty(provisioner.UpdatedEmails);
    }

    [Fact]
    public async Task Update_EmailChange_ProviderUpdateFirst_ThenDbWrite()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var row = await SeedRowAsync(users, "2661", subject: "subject-1");
        provisioner.Seed(new ProviderUser("subject-1", "joao@dmo.test"));
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.UpdateAsync(
            new UserAdministrationCommands.UpdateUserCommand(
                row.AccountId, "João Silva", "2661", "novo@dmo.test", "Reparador", ExpectedVersion: 1),
            CancellationToken.None);

        var success = Assert.IsType<UserAdministrationResult.Success>(result);
        Assert.Equal("novo@dmo.test", success.Account.Email);
        Assert.Equal(("subject-1", "novo@dmo.test"), Assert.Single(provisioner.UpdatedEmails));
        Assert.Equal("novo@dmo.test", users.Accounts.Single().Email);
    }

    [Fact]
    public async Task Update_EmailChange_ProviderFailure_Aborts_NoDbWrite()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner
        {
            UpdateEmailFailure = new ProviderUserOperationException(ProviderUserOperationFailure.Error, "boom"),
        };
        var row = await SeedRowAsync(users, "2661", subject: "subject-1");
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.UpdateAsync(
            new UserAdministrationCommands.UpdateUserCommand(
                row.AccountId, "João Silva", "2661", "novo@dmo.test", "Reparador", ExpectedVersion: 1),
            CancellationToken.None);

        var failed = Assert.IsType<UserAdministrationResult.ProviderFailed>(result);
        Assert.Equal(ProviderFailureKind.Error, failed.Kind);
        Assert.Equal("joao@dmo.test", users.Accounts.Single().Email);
    }

    [Fact]
    public async Task Update_EmailChange_StaleDbWrite_CompensatesRestoringOldEmail()
    {
        var users = new FakeUserRepository
        {
            UpdateFailure = new ConcurrencyConflictException("concurrent writer"),
        };
        var provisioner = new FakeUserIdentityProvisioner();
        var row = await SeedRowAsync(users, "2661", subject: "subject-1");
        provisioner.Seed(new ProviderUser("subject-1", "joao@dmo.test"));
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.UpdateAsync(
            new UserAdministrationCommands.UpdateUserCommand(
                row.AccountId, "João Silva", "2661", "novo@dmo.test", "Reparador", ExpectedVersion: 1),
            CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.StaleVersion, conflict.Reason);
        // Compensation: the provider carrier is restored to the former email after the stale
        // DB write (best effort); the conflict is explicit, never silent success.
        Assert.Equal(
            new[] { ("subject-1", "novo@dmo.test"), ("subject-1", "joao@dmo.test") },
            provisioner.UpdatedEmails.ToArray());
    }

    [Fact]
    public async Task Update_StaleVersion_Conflict_BeforeProviderCall()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var row = await SeedRowAsync(users, "2661", subject: "subject-1", version: 2);
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.UpdateAsync(
            new UserAdministrationCommands.UpdateUserCommand(
                row.AccountId, "João Silva", "2661", "novo@dmo.test", "Reparador", ExpectedVersion: 1),
            CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.StaleVersion, conflict.Reason);
        Assert.Empty(provisioner.UpdatedEmails);
    }

    [Fact]
    public async Task Update_DuplicateCompanyNumber_Conflict()
    {
        var users = new FakeUserRepository();
        var row = await SeedRowAsync(users, "2661", subject: "subject-1");
        await SeedRowAsync(users, "7777", subject: "subject-2");
        var service = CreateService(users, new FakeTemplateRepository(), new FakeUserIdentityProvisioner());

        var result = await service.UpdateAsync(
            new UserAdministrationCommands.UpdateUserCommand(
                row.AccountId, "João Silva", "7777", "joao@dmo.test", "Reparador", ExpectedVersion: 1),
            CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.DuplicateCompanyNumber, conflict.Reason);
    }

    [Fact]
    public async Task Update_DuplicateEmail_Conflict()
    {
        var users = new FakeUserRepository();
        var row = await SeedRowAsync(users, "2661", subject: "subject-1");
        await SeedRowAsync(users, "7777", subject: "subject-2", email: "outro@dmo.test");
        var service = CreateService(users, new FakeTemplateRepository(), new FakeUserIdentityProvisioner());

        var result = await service.UpdateAsync(
            new UserAdministrationCommands.UpdateUserCommand(
                row.AccountId, "João Silva", "2661", "outro@dmo.test", "Reparador", ExpectedVersion: 1),
            CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.EmailInUse, conflict.Reason);
    }

    [Fact]
    public async Task Update_MissingUser_NotFound()
    {
        var service = CreateService(new FakeUserRepository(), new FakeTemplateRepository(), new FakeUserIdentityProvisioner());

        var result = await service.UpdateAsync(
            new UserAdministrationCommands.UpdateUserCommand(
                Guid.NewGuid(), "João Silva", "2661", "joao@dmo.test", "Reparador", ExpectedVersion: 1),
            CancellationToken.None);

        Assert.IsType<UserAdministrationResult.NotFound>(result);
    }

    // ------------------------------------------------------------------ ACTIVATE / DEACTIVATE

    [Fact]
    public async Task Activate_And_Deactivate_UseExpectedVersion()
    {
        var users = new FakeUserRepository();
        var row = await SeedRowAsync(users, "2661", active: false);
        var service = CreateService(users, new FakeTemplateRepository(), new FakeUserIdentityProvisioner());

        var activate = await service.SetActiveAsync(
            new UserAdministrationCommands.SetUserActiveCommand(row.AccountId, Active: true, ExpectedVersion: 1),
            CancellationToken.None);
        var active = Assert.IsType<UserAdministrationResult.Success>(activate);
        Assert.True(active.Account.IsActive);
        Assert.Equal(2, active.Account.Version);

        var deactivate = await service.SetActiveAsync(
            new UserAdministrationCommands.SetUserActiveCommand(row.AccountId, Active: false, ExpectedVersion: 2),
            CancellationToken.None);
        var inactive = Assert.IsType<UserAdministrationResult.Success>(deactivate);
        Assert.False(inactive.Account.IsActive);
    }

    [Fact]
    public async Task Activate_StaleVersion_Conflict()
    {
        var users = new FakeUserRepository();
        var row = await SeedRowAsync(users, "2661", active: false, version: 2);
        var service = CreateService(users, new FakeTemplateRepository(), new FakeUserIdentityProvisioner());

        var result = await service.SetActiveAsync(
            new UserAdministrationCommands.SetUserActiveCommand(row.AccountId, Active: true, ExpectedVersion: 1),
            CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.StaleVersion, conflict.Reason);
        Assert.False(users.Accounts.Single().IsActive);
    }

    [Fact]
    public async Task Activate_MissingUser_NotFound()
    {
        var service = CreateService(new FakeUserRepository(), new FakeTemplateRepository(), new FakeUserIdentityProvisioner());

        var result = await service.SetActiveAsync(
            new UserAdministrationCommands.SetUserActiveCommand(Guid.NewGuid(), Active: true, ExpectedVersion: 1),
            CancellationToken.None);

        Assert.IsType<UserAdministrationResult.NotFound>(result);
    }

    // ------------------------------------------------------------------ TEMPLATE ASSIGNMENT

    [Fact]
    public async Task Template_Assign_Reassign_Remove_SingleRelationship()
    {
        var templates = new FakeTemplateRepository();
        templates.Seed(new Template(TemplateId, "Manutenção", null, Version: 1));
        var otherTemplateId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        templates.Seed(new Template(otherTemplateId, "Produção", null, Version: 1));
        var users = new FakeUserRepository();
        var row = await SeedRowAsync(users, "2661");
        var service = CreateService(users, templates, new FakeUserIdentityProvisioner());

        var assign = await service.SetTemplateAsync(
            new UserAdministrationCommands.SetUserTemplateCommand(row.AccountId, TemplateId, ExpectedVersion: 1),
            CancellationToken.None);
        Assert.Equal(TemplateId, Assert.IsType<UserAdministrationResult.Success>(assign).Account.TemplateId);

        var reassign = await service.SetTemplateAsync(
            new UserAdministrationCommands.SetUserTemplateCommand(row.AccountId, otherTemplateId, ExpectedVersion: 2),
            CancellationToken.None);
        Assert.Equal(otherTemplateId, Assert.IsType<UserAdministrationResult.Success>(reassign).Account.TemplateId);
        Assert.Single(users.Accounts); // one relationship only — never a union

        var remove = await service.SetTemplateAsync(
            new UserAdministrationCommands.SetUserTemplateCommand(row.AccountId, null, ExpectedVersion: 3),
            CancellationToken.None);
        Assert.Null(Assert.IsType<UserAdministrationResult.Success>(remove).Account.TemplateId);
    }

    [Fact]
    public async Task Template_UnknownTemplate_ValidationFailed()
    {
        var users = new FakeUserRepository();
        var row = await SeedRowAsync(users, "2661");
        var service = CreateService(users, new FakeTemplateRepository(), new FakeUserIdentityProvisioner());

        var result = await service.SetTemplateAsync(
            new UserAdministrationCommands.SetUserTemplateCommand(row.AccountId, Guid.NewGuid(), ExpectedVersion: 1),
            CancellationToken.None);

        Assert.IsType<UserAdministrationResult.ValidationFailed>(result);
        Assert.Null(users.Accounts.Single().TemplateId);
    }

    [Fact]
    public async Task Template_Stale_Conflict()
    {
        var templates = new FakeTemplateRepository();
        templates.Seed(new Template(TemplateId, "Manutenção", null, Version: 1));
        var users = new FakeUserRepository();
        var row = await SeedRowAsync(users, "2661", version: 2);
        var service = CreateService(users, templates, new FakeUserIdentityProvisioner());

        var result = await service.SetTemplateAsync(
            new UserAdministrationCommands.SetUserTemplateCommand(row.AccountId, TemplateId, ExpectedVersion: 1),
            CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.StaleVersion, conflict.Reason);
    }

    // ------------------------------------------------------------------ DELETE

    [Fact]
    public async Task Delete_StalePreCheck_NoProviderCall_NoDbDelete()
    {
        // Correction A: the non-destructive version pre-check rejects a stale confirmation
        // BEFORE any provider call and BEFORE any row delete.
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var row = await SeedRowAsync(users, "2661", subject: "subject-1", version: 2);
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.DeleteAsync(
            new UserAdministrationCommands.DeleteUserCommand(row.AccountId, ExpectedVersion: 1),
            CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.StaleVersion, conflict.Reason);
        Assert.Empty(provisioner.DeletedSubjects);
        Assert.Single(users.Accounts);
    }

    [Fact]
    public async Task Delete_ProviderFirst_ThenDb_OnExpectedVersion()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var row = await SeedRowAsync(users, "2661", subject: "subject-1");
        provisioner.Seed(new ProviderUser("subject-1", "joao@dmo.test"));
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.DeleteAsync(
            new UserAdministrationCommands.DeleteUserCommand(row.AccountId, ExpectedVersion: 1),
            CancellationToken.None);

        Assert.IsType<UserAdministrationResult.Success>(result);
        Assert.Equal("subject-1", Assert.Single(provisioner.DeletedSubjects));
        Assert.Empty(users.Accounts);
    }

    [Fact]
    public async Task Delete_ProviderFailure_Aborts_DbUntouched()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner
        {
            DeleteFailure = new ProviderUserOperationException(ProviderUserOperationFailure.Error, "boom"),
        };
        var row = await SeedRowAsync(users, "2661", subject: "subject-1");
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.DeleteAsync(
            new UserAdministrationCommands.DeleteUserCommand(row.AccountId, ExpectedVersion: 1),
            CancellationToken.None);

        var failed = Assert.IsType<UserAdministrationResult.ProviderFailed>(result);
        Assert.Equal(ProviderFailureKind.Error, failed.Kind);
        Assert.Single(users.Accounts);
    }

    [Fact]
    public async Task Delete_ProviderAlreadyAbsent_Idempotent_Continues()
    {
        // Provider 404 (= already absent) counts as provider success; the DB delete proceeds.
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner(); // provider store empty: already absent
        var row = await SeedRowAsync(users, "2661", subject: "subject-gone");
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.DeleteAsync(
            new UserAdministrationCommands.DeleteUserCommand(row.AccountId, ExpectedVersion: 1),
            CancellationToken.None);

        Assert.IsType<UserAdministrationResult.Success>(result);
        Assert.Equal("subject-gone", Assert.Single(provisioner.DeletedSubjects));
        Assert.Empty(users.Accounts);
    }

    [Fact]
    public async Task Delete_ProviderOk_FinalDbDeleteStale_RowSurvives_ExplicitPartialResult()
    {
        // Accepted delete race (Correction A): the pre-check passes, another writer updates
        // the row, the provider delete succeeds and the final version-checked row delete
        // fails stale. The row survives with its provider linkage (retry anchor) and the
        // result is explicit, never success.
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var row = await SeedRowAsync(users, "2661", subject: "subject-1");
        provisioner.Seed(new ProviderUser("subject-1", "joao@dmo.test"));

        // Simulate the concurrent writer bumping the version between the pre-check (step 2)
        // and the final row delete (step 4).
        users.BeforeVersionedDelete = id =>
        {
            var current = users.Accounts.Single(account => account.AccountId == id);
            users.SetActiveAsync(id, current.IsActive, current.Version, CancellationToken.None)
                .GetAwaiter().GetResult();
        };

        var service = CreateService(users, new FakeTemplateRepository(), provisioner);
        var result = await service.DeleteAsync(
            new UserAdministrationCommands.DeleteUserCommand(row.AccountId, ExpectedVersion: 1),
            CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.StaleVersion, conflict.Reason);
        Assert.Contains("retry", conflict.Message, StringComparison.OrdinalIgnoreCase);

        // The provider identity was removed and the row survives WITH the retry anchor.
        Assert.Equal("subject-1", Assert.Single(provisioner.DeletedSubjects));
        var surviving = Assert.Single(users.Accounts);
        Assert.Equal(2, surviving.Version);
        Assert.Equal("subject-1", users.SubjectOf(surviving.AccountId));
    }

    [Fact]
    public async Task Delete_RetryAfterProviderAlreadyAbsent_CompletesDeletion()
    {
        // Continuation of the recoverable partial state: on retry the provider identity is
        // already absent (404 = idempotent provider success) and the row delete completes
        // with the current version.
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var row = await SeedRowAsync(users, "2661", subject: "subject-1");
        provisioner.Seed(new ProviderUser("subject-1", "joao@dmo.test"));
        users.BeforeVersionedDelete = id =>
        {
            var current = users.Accounts.Single(account => account.AccountId == id);
            users.SetActiveAsync(id, current.IsActive, current.Version, CancellationToken.None)
                .GetAwaiter().GetResult();
        };
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var first = await service.DeleteAsync(
            new UserAdministrationCommands.DeleteUserCommand(row.AccountId, ExpectedVersion: 1),
            CancellationToken.None);
        Assert.IsType<UserAdministrationResult.Conflict>(first);

        // Retry with the current version: provider already absent -> proceeds; row removed.
        users.BeforeVersionedDelete = null;
        var current = users.Accounts.Single();
        var retry = await service.DeleteAsync(
            new UserAdministrationCommands.DeleteUserCommand(current.AccountId, ExpectedVersion: current.Version),
            CancellationToken.None);

        Assert.IsType<UserAdministrationResult.Success>(retry);
        Assert.Empty(users.Accounts);
    }

    [Fact]
    public async Task Delete_MissingUser_NotFound()
    {
        var service = CreateService(new FakeUserRepository(), new FakeTemplateRepository(), new FakeUserIdentityProvisioner());

        var result = await service.DeleteAsync(
            new UserAdministrationCommands.DeleteUserCommand(Guid.NewGuid(), ExpectedVersion: 1),
            CancellationToken.None);

        Assert.IsType<UserAdministrationResult.NotFound>(result);
    }

    // ------------------------------------------------------------------ PASSWORD RESET

    [Fact]
    public async Task Reset_ActiveUser_InitiatesRecovery_WithCarrierEmail()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var row = await SeedRowAsync(users, "2661", active: true, subject: "subject-1");
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.InitiatePasswordResetAsync(
            new UserAdministrationCommands.RequestedPasswordResetCommand(row.AccountId, ExpectedVersion: 1),
            CancellationToken.None);

        Assert.IsType<UserAdministrationResult.Success>(result);
        Assert.Equal("joao@dmo.test", Assert.Single(provisioner.RecoveryEmails));
    }

    [Fact]
    public async Task Reset_InactiveUser_ValidationFailed_NoProviderCall()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var row = await SeedRowAsync(users, "2661", active: false, subject: "subject-1");
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.InitiatePasswordResetAsync(
            new UserAdministrationCommands.RequestedPasswordResetCommand(row.AccountId, ExpectedVersion: 1),
            CancellationToken.None);

        Assert.IsType<UserAdministrationResult.ValidationFailed>(result);
        Assert.Empty(provisioner.RecoveryEmails);
    }

    [Fact]
    public async Task Reset_StaleFicha_Conflict_NoProviderCall()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner();
        var row = await SeedRowAsync(users, "2661", version: 2);
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.InitiatePasswordResetAsync(
            new UserAdministrationCommands.RequestedPasswordResetCommand(row.AccountId, ExpectedVersion: 1),
            CancellationToken.None);

        var conflict = Assert.IsType<UserAdministrationResult.Conflict>(result);
        Assert.Equal(UserConflictReason.StaleVersion, conflict.Reason);
        Assert.Empty(provisioner.RecoveryEmails);
    }

    [Fact]
    public async Task Reset_MissingUser_NotFound()
    {
        var service = CreateService(new FakeUserRepository(), new FakeTemplateRepository(), new FakeUserIdentityProvisioner());

        var result = await service.InitiatePasswordResetAsync(
            new UserAdministrationCommands.RequestedPasswordResetCommand(Guid.NewGuid(), ExpectedVersion: 1),
            CancellationToken.None);

        Assert.IsType<UserAdministrationResult.NotFound>(result);
    }

    [Fact]
    public async Task Reset_ProviderFailure_ProviderFailed()
    {
        var users = new FakeUserRepository();
        var provisioner = new FakeUserIdentityProvisioner
        {
            RecoveryFailure = new ProviderUserOperationException(ProviderUserOperationFailure.Error, "boom"),
        };
        var row = await SeedRowAsync(users, "2661", active: true, subject: "subject-1");
        var service = CreateService(users, new FakeTemplateRepository(), provisioner);

        var result = await service.InitiatePasswordResetAsync(
            new UserAdministrationCommands.RequestedPasswordResetCommand(row.AccountId, ExpectedVersion: 1),
            CancellationToken.None);

        Assert.IsType<UserAdministrationResult.ProviderFailed>(result);
    }

    // ------------------------------------------------------------------ helpers

    private static UserAdministrationService CreateService(
        FakeUserRepository users,
        FakeTemplateRepository templates,
        FakeUserIdentityProvisioner provisioner) =>
        new(users, templates, provisioner);

    private static UserAdministrationCommands.CreateUserCommand Command() => new(
        Name: "João Silva",
        CompanyNumber: "2661",
        Email: "joao@dmo.test",
        Role: "Reparador",
        Active: true,
        TemplateId: null,
        InviteRedirectUrl: null);

    private static async Task<UserAccount> SeedRowAsync(
        FakeUserRepository users,
        string companyNumber,
        bool active = true,
        Guid? templateId = null,
        string? name = null,
        string? email = null,
        string? role = null,
        string? subject = null,
        int version = 1)
    {
        var account = new UserAccount(
            Guid.NewGuid(),
            CompanyNumber: companyNumber,
            DisplayName: name ?? "João Silva",
            Email: email ?? "joao@dmo.test",
            RoleLabel: role ?? "Reparador",
            IsActive: active,
            TemplateId: templateId,
            Version: version);

        await users.CreatedAsync(account, subject ?? $"subject-seeded-{Guid.NewGuid():N}", CancellationToken.None);

        if (version > 1)
        {
            var row = (await users.GetByIdAsync(account.AccountId, CancellationToken.None))!;
            for (var current = row.Version; current < version; current++)
            {
                await users.SetActiveAsync(account.AccountId, row.IsActive, current, CancellationToken.None);
            }
        }

        return account;
    }
}