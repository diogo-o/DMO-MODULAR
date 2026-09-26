using System.Net;
using System.Text.Json;
using DMO.Application.Controlo.Pesos;
using DMO.Application.Controlo.Settings;
using DMO.IntegrationTests.Controlo.Pesos;

namespace DMO.IntegrationTests.Controlo.Settings;

/// <summary>
/// P2-T05 transport-class (<c>I</c>) proofs of the <b>remaining</b> Definições surface (routes
/// 15–17): the REAL web host with the REAL <c>ControloDefinicoesEndpoints</c>/Razor page and the
/// REAL <c>ControloDefinicoesService</c> over the controlled P2-T05 store.
/// </summary>
/// <remarks>
/// <para>
/// Authority: P2-T05 contract §21.3 (routes 15–17), §26.2 (the exact validation tokens), §26.4 row
/// MAC7 (AC-E5) and AC-G2 (the settings authorization proof). Every caller in this class holds the
/// <c>controlo-create</c> grant.</para>
/// <para>
/// <b>Superseded (Owner clarification P2-T07 §34.3 / P2-T05 §31.3):</b> the repairer register and
/// the machine → repairer assignment surface (the old routes 13/14) MOVED to
/// <c>Boquilhas > Definições</c> — their transport/behavior proofs now live in
/// <c>BoquilhasDefinicoesEndpointsTests</c> (same physical tables; moved ownership/service/UI).</para>
/// <para>
/// The transport rows prove the exact round-trips: the PDF directory is explicit about its
/// not-configured state and uses the typed check vocabulary, and the email lists/templates
/// replace-all and delete with explicit confirmation.</para>
/// </remarks>
public sealed class ControloDefinicoesEndpointsTests
{
    private const string DefinicoesPath = "/controlo/create/definicoes";

    /// <summary>
    /// PDF-directory transport (route 15, supports MAC7/settings transport): the not-configured
    /// state is explicit (<c>baseDirectory:null, version:null</c>), a rooted server-host path is
    /// saved (version 1), blank/non-rooted values are refused with
    /// <c>DIRECTORY_REQUIRED</c>/<c>DIRECTORY_INVALID</c>, and the server-side check reports the
    /// TYPED §12.2 vocabulary only (SET3-transport: states are never conflated).
    /// </summary>
    [Fact]
    public async Task PdfDirectory_TransportRoundTripWithTheTypedCheckVocabulary()
    {
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        using (var notConfigured = await P2T05TestHost.GetAsync(client, $"{DefinicoesPath}/pdf-directory"))
        {
            Assert.Equal(HttpStatusCode.OK, notConfigured.StatusCode);

            var payload = await ReadJsonAsync(notConfigured);
            Assert.Equal(JsonValueKind.Null, payload.GetProperty("baseDirectory").ValueKind);
            Assert.Equal(JsonValueKind.Null, payload.GetProperty("version").ValueKind);
        }

        // The configured value is server-host absolute; the temp-based root is rooted everywhere.
        var baseDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "dmo-reports"));
        Assert.True(Path.IsPathRooted(baseDirectory));

        using (var saved = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{DefinicoesPath}/pdf-directory",
                   P2T05TestHost.Json(new { baseDirectory, expectedVersion = (int?)null })))
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

            var payload = await ReadJsonAsync(saved);
            Assert.Equal(1, payload.GetProperty("version").GetInt32());
        }

        using (var readBack = await P2T05TestHost.GetAsync(client, $"{DefinicoesPath}/pdf-directory"))
        {
            var payload = await ReadJsonAsync(readBack);
            Assert.Equal(baseDirectory, payload.GetProperty("baseDirectory").GetString());
            Assert.Equal(1, payload.GetProperty("version").GetInt32());
        }

        // Blank and relative values are validation failures, never writes.
        using (var blank = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{DefinicoesPath}/pdf-directory",
                   P2T05TestHost.Json(new { baseDirectory = "   ", expectedVersion = 1 })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

            var payload = await ReadJsonAsync(blank);
            Assert.Equal("validation-failed", payload.GetProperty("reason").GetString());
            Assert.Contains(
                ControloDefinicoesValidationErrors.DirectoryRequired,
                payload.GetProperty("errors").EnumerateArray().Select(error => error.GetString() ?? string.Empty));
        }

        using (var relative = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{DefinicoesPath}/pdf-directory",
                   P2T05TestHost.Json(new { baseDirectory = "reports", expectedVersion = 1 })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, relative.StatusCode);

            var payload = await ReadJsonAsync(relative);
            Assert.Equal("validation-failed", payload.GetProperty("reason").GetString());
            Assert.Contains(
                ControloDefinicoesValidationErrors.DirectoryInvalid,
                payload.GetProperty("errors").EnumerateArray().Select(error => error.GetString() ?? string.Empty));
        }

        // The typed check vocabulary: every verdict is its own token, never conflated (SET3).
        foreach (var (verdict, expectedToken) in new[]
                 {
                     (PdfDirectoryCheckState.NotADirectory, "not-a-directory"),
                     (PdfDirectoryCheckState.DirectoryNotFound, "directory-not-found"),
                     (PdfDirectoryCheckState.Ok, "ok"),
                 })
        {
            composition.DirectoryProbe.Verdict = verdict;

            using var check = await P2T05TestHost.SendJsonAsync(
                client, HttpMethod.Post, $"{DefinicoesPath}/pdf-directory/check", "{}");
            Assert.Equal(HttpStatusCode.OK, check.StatusCode);

            var payload = await ReadJsonAsync(check);
            Assert.Equal(expectedToken, payload.GetProperty("state").GetString());
        }
    }

    /// <summary>
    /// Email-list transport (route 16): create → 201 with the real id; a duplicate name → 409
    /// <c>duplicate-name</c>; the list surface reports the recipient COUNT; the ficha read returns
    /// the COMPLETE recipient set; an update replaces the whole set in one transaction; a delete
    /// requires the explicit confirmation and a second delete of the same id is a 404; an invalid
    /// address is <c>ADDRESS_INVALID</c>.
    /// </summary>
    [Fact]
    public async Task EmailLists_TransportRoundTripWithReplaceAllAndExplicitDelete()
    {
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        Guid listId;
        using (var created = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{DefinicoesPath}/email-lists",
                   P2T05TestHost.Json(new
                   {
                       name = "Fábrica",
                       recipients = new[] { "a@x.pt", "b@x.pt" },
                   })))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var payload = await ReadJsonAsync(created);
            listId = payload.GetProperty("emailListId").GetGuid();
            Assert.NotEqual(Guid.Empty, listId);
            Assert.Equal(1, payload.GetProperty("version").GetInt32());
        }

        using (var duplicate = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{DefinicoesPath}/email-lists",
                   P2T05TestHost.Json(new
                   {
                       name = "Fábrica",
                       recipients = new[] { "c@x.pt" },
                   })))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

            var payload = await ReadJsonAsync(duplicate);
            Assert.Equal("duplicate-name", payload.GetProperty("reason").GetString());
        }

        using (var list = await P2T05TestHost.GetAsync(client, $"{DefinicoesPath}/email-lists"))
        {
            var payload = await ReadJsonAsync(list);
            var item = Assert.Single(payload.GetProperty("lists").EnumerateArray().ToArray());
            Assert.Equal(listId, item.GetProperty("emailListId").GetGuid());
            Assert.Equal("Fábrica", item.GetProperty("name").GetString());
            Assert.Equal(2, item.GetProperty("recipientCount").GetInt32());
            Assert.Equal(1, item.GetProperty("version").GetInt32());
        }

        using (var ficha = await P2T05TestHost.GetAsync(client, $"{DefinicoesPath}/email-lists/{listId}"))
        {
            var payload = await ReadJsonAsync(ficha);
            Assert.Equal("Fábrica", payload.GetProperty("name").GetString());
            Assert.Equal(
                new[] { "a@x.pt", "b@x.pt" },
                payload.GetProperty("recipients").EnumerateArray().Select(recipient => recipient.GetString()));
        }

        // Replace-all: the update carries the COMPLETE new set; the old addresses are gone.
        using (var updated = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{DefinicoesPath}/email-lists/{listId}",
                   P2T05TestHost.Json(new
                   {
                       expectedVersion = 1,
                       name = "Fábrica",
                       recipients = new[] { "c@x.pt" },
                   })))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

            var payload = await ReadJsonAsync(updated);
            Assert.Equal(listId, payload.GetProperty("emailListId").GetGuid());
            Assert.Equal(2, payload.GetProperty("version").GetInt32());
        }

        using (var afterReplace = await P2T05TestHost.GetAsync(client, $"{DefinicoesPath}/email-lists/{listId}"))
        {
            var payload = await ReadJsonAsync(afterReplace);
            Assert.Equal(
                new[] { "c@x.pt" },
                payload.GetProperty("recipients").EnumerateArray().Select(recipient => recipient.GetString()));
        }

        // Confirmed delete → 204; the same id again → 404; unconfirmed delete → 400
        // DELETE_NOT_CONFIRMED and the list survives.
        using (var deleted = await P2T05TestHost.SendAuthenticatedAsync(
                   client,
                   new HttpRequestMessage(
                       HttpMethod.Delete,
                       $"{DefinicoesPath}/email-lists/{listId}?expectedVersion=2&deleteConfirmed=true")))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using (var again = await P2T05TestHost.SendAuthenticatedAsync(
                   client,
                   new HttpRequestMessage(
                       HttpMethod.Delete,
                       $"{DefinicoesPath}/email-lists/{listId}?expectedVersion=2&deleteConfirmed=true")))
        {
            Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        }

        using (var invalidAddress = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{DefinicoesPath}/email-lists",
                   P2T05TestHost.Json(new
                   {
                       name = "Turno",
                       recipients = new[] { "not-an-address" },
                   })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalidAddress.StatusCode);

            var payload = await ReadJsonAsync(invalidAddress);
            Assert.Equal("validation-failed", payload.GetProperty("reason").GetString());
            Assert.Contains(
                ControloDefinicoesValidationErrors.AddressInvalid,
                payload.GetProperty("errors").EnumerateArray().Select(error => error.GetString() ?? string.Empty));
        }

        // The freed name can be re-created, and the unconfirmed delete leaves it intact.
        Guid secondId;
        using (var recreated = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{DefinicoesPath}/email-lists",
                   P2T05TestHost.Json(new
                   {
                       name = "Fábrica",
                       recipients = new[] { "a@x.pt" },
                   })))
        {
            Assert.Equal(HttpStatusCode.Created, recreated.StatusCode);
            secondId = (await ReadJsonAsync(recreated)).GetProperty("emailListId").GetGuid();
        }

        using (var unconfirmed = await P2T05TestHost.SendAuthenticatedAsync(
                   client,
                   new HttpRequestMessage(
                       HttpMethod.Delete,
                       $"{DefinicoesPath}/email-lists/{secondId}?expectedVersion=1&deleteConfirmed=false")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);

            var payload = await ReadJsonAsync(unconfirmed);
            Assert.Equal("validation-failed", payload.GetProperty("reason").GetString());
            Assert.Contains(
                ControloDefinicoesValidationErrors.DeleteNotConfirmed,
                payload.GetProperty("errors").EnumerateArray().Select(error => error.GetString() ?? string.Empty));
        }

        using (var survives = await P2T05TestHost.GetAsync(client, $"{DefinicoesPath}/email-lists"))
        {
            var payload = await ReadJsonAsync(survives);
            Assert.Equal(1, payload.GetProperty("lists").GetArrayLength());
            Assert.Equal(secondId, payload.GetProperty("lists")[0].GetProperty("emailListId").GetGuid());
        }
    }

    /// <summary>
    /// Email-template transport (route 17): create with the <c>peso</c> document type → 201; an
    /// unknown document type → 400 <c>DOCUMENT_TYPE_UNKNOWN</c>; a null document type is the clean
    /// generic template → 201; the rename keeps the id and bumps to version 2; the confirmed delete
    /// → 204.
    /// </summary>
    [Fact]
    public async Task EmailTemplates_TransportRoundTripWithTheDocumentTypeVocabulary()
    {
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        Guid templateId;
        using (var created = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{DefinicoesPath}/email-templates",
                   P2T05TestHost.Json(new
                   {
                       name = "Peso",
                       subject = "Relatório",
                       body = "Em anexo",
                       documentType = "peso",
                   })))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var payload = await ReadJsonAsync(created);
            templateId = payload.GetProperty("emailTemplateId").GetGuid();
            Assert.NotEqual(Guid.Empty, templateId);
            Assert.Equal(1, payload.GetProperty("version").GetInt32());
        }

        using (var unknownType = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{DefinicoesPath}/email-templates",
                   P2T05TestHost.Json(new
                   {
                       name = "Inválido",
                       subject = "S",
                       body = "B",
                       documentType = "x",
                   })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unknownType.StatusCode);

            var payload = await ReadJsonAsync(unknownType);
            Assert.Equal("validation-failed", payload.GetProperty("reason").GetString());
            Assert.Contains(
                ControloDefinicoesValidationErrors.DocumentTypeUnknown,
                payload.GetProperty("errors").EnumerateArray().Select(error => error.GetString() ?? string.Empty));
        }

        using (var generic = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{DefinicoesPath}/email-templates",
                   P2T05TestHost.Json(new
                   {
                       name = "Genérico",
                       subject = "S",
                       body = "B",
                       documentType = (string?)null,
                   })))
        {
            Assert.Equal(HttpStatusCode.Created, generic.StatusCode);
        }

        using (var renamed = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"{DefinicoesPath}/email-templates/{templateId}",
                   P2T05TestHost.Json(new
                   {
                       expectedVersion = 1,
                       name = "Peso atualizado",
                       subject = "Relatório",
                       body = "Em anexo (revisto)",
                       documentType = "peso",
                   })))
        {
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

            var payload = await ReadJsonAsync(renamed);
            Assert.Equal(templateId, payload.GetProperty("emailTemplateId").GetGuid());
            Assert.Equal(2, payload.GetProperty("version").GetInt32());
        }

        using (var deleted = await P2T05TestHost.SendAuthenticatedAsync(
                   client,
                   new HttpRequestMessage(
                       HttpMethod.Delete,
                       $"{DefinicoesPath}/email-templates/{templateId}?expectedVersion=2&deleteConfirmed=true")))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        // The generic template is the only survivor.
        using (var list = await P2T05TestHost.GetAsync(client, $"{DefinicoesPath}/email-templates"))
        {
            var payload = await ReadJsonAsync(list);
            var items = payload.GetProperty("templates").EnumerateArray().ToArray();
            var item = Assert.Single(items);
            Assert.Equal("Genérico", item.GetProperty("name").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("documentType").ValueKind);
        }
    }

    // ---- arrangement helpers -------------------------------------------------------------

    /// <summary>
    /// P2-T08 email slice — the template group routing transports as a COMPLETE pair (machine
    /// group B/C + the configured recipient list): a create with an existing list and group B
    /// persists and reads back both members; a group without its list (or vice versa) and an
    /// unknown list id are <c>EMAIL_LIST_NOT_FOUND</c>; an unknown group is
    /// <c>MACHINE_GROUP_UNKNOWN</c>.
    /// </summary>
    [Fact]
    public async Task EmailTemplates_GroupRoutingRequiresTheGroupAndExistingListTogether()
    {
        var composition = new P2T05TestComposition();

        using var factory = P2T05TestHost.ForUser(P2T05TestHost.AllGranted(), composition);
        using var client = factory.CreateClient();

        // Create the configured list the routing will reference.
        Guid listId;
        using (var list = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{DefinicoesPath}/email-lists",
                   P2T05TestHost.Json(new
                   {
                       name = "Emails B",
                       recipients = new[] { "ops.b@example.com" },
                   })))
        {
            Assert.Equal(HttpStatusCode.Created, list.StatusCode);
            listId = (await ReadJsonAsync(list)).GetProperty("emailListId").GetGuid();
        }

        Guid templateId;
        using (var created = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{DefinicoesPath}/email-templates",
                   P2T05TestHost.Json(new
                   {
                       name = "Template B",
                       subject = "Peso grupo B",
                       body = "Segue o Peso.",
                       documentType = "peso",
                       machineGroup = "B",
                       emailListId = listId,
                   })))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            templateId = (await ReadJsonAsync(created)).GetProperty("emailTemplateId").GetGuid();
        }

        // The read-back carries BOTH routing members.
        using (var read = await P2T05TestHost.GetAsync(client, $"{DefinicoesPath}/email-templates/{templateId}"))
        {
            var payload = await ReadJsonAsync(read);
            Assert.Equal("B", payload.GetProperty("machineGroup").GetString());
            Assert.Equal(listId, payload.GetProperty("emailListId").GetGuid());
        }

        // The list listing shows the routing columns (group + associated list name) through the
        // page's data source.
        using (var templates = await P2T05TestHost.GetAsync(client, $"{DefinicoesPath}/email-templates"))
        {
            var item = Assert.Single((await ReadJsonAsync(templates)).GetProperty("templates").EnumerateArray());
            Assert.Equal("B", item.GetProperty("machineGroup").GetString());
            Assert.Equal(listId, item.GetProperty("emailListId").GetGuid());
        }

        // A group without its list → 400 EMAIL_LIST_NOT_FOUND (incomplete routing).
        Guid? noListId = null;
        using (var groupOnly = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{DefinicoesPath}/email-templates",
                   P2T05TestHost.Json(new
                   {
                       name = "Tpl B sem lista",
                       subject = "S",
                       body = "B",
                       documentType = "peso",
                       machineGroup = "B",
                       emailListId = noListId,
                   })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, groupOnly.StatusCode);
            var errors = (await ReadJsonAsync(groupOnly)).GetProperty("errors").EnumerateArray()
                .Select(error => error.GetString()).ToArray();
            Assert.Contains(ControloDefinicoesValidationErrors.EmailListNotFound, errors);
        }

        // An unknown list id → 400 EMAIL_LIST_NOT_FOUND.
        using (var unknownList = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{DefinicoesPath}/email-templates",
                   P2T05TestHost.Json(new
                   {
                       name = "Tpl B lista inexistente",
                       subject = "S",
                       body = "B",
                       documentType = "peso",
                       machineGroup = "B",
                       emailListId = Guid.NewGuid(),
                   })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unknownList.StatusCode);
            Assert.Contains(
                ControloDefinicoesValidationErrors.EmailListNotFound,
                (await ReadJsonAsync(unknownList)).GetProperty("errors").EnumerateArray()
                    .Select(error => error.GetString()));
        }

        // An unknown machine group → 400 MACHINE_GROUP_UNKNOWN (only B/C exist).
        using (var unknownGroup = await P2T05TestHost.SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"{DefinicoesPath}/email-templates",
                   P2T05TestHost.Json(new
                   {
                       name = "Tpl X",
                       subject = "S",
                       body = "B",
                       documentType = (string?)null,
                       machineGroup = "X",
                       emailListId = listId,
                   })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unknownGroup.StatusCode);
            Assert.Contains(
                ControloDefinicoesValidationErrors.MachineGroupUnknown,
                (await ReadJsonAsync(unknownGroup)).GetProperty("errors").EnumerateArray()
                    .Select(error => error.GetString()));
        }
    }

    /// <summary>Reads a JSON response body into a detached element.</summary>
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}