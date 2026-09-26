using System.Reflection;
using System.Runtime.CompilerServices;
using DMO.Domain.Controlo;
using DMO.Domain.Tools;

namespace DMO.UnitTests.Controlo.Settings;

/// <summary>
/// P2-T05 unit/shape proofs of the machine and settings surface: rows MAC6 and SET10 of the
/// test-to-acceptance matrix (<c>plans/contracts/P2-T05_CONTROLO_CREATE_CONTRACT.md</c> §26.4).
/// The six machines are independent operational codes with no line/group concept (AC-E4), and every
/// Definições record is a site-wide configuration row with no per-user dimension (AC-F8, Q-SITE).
/// </summary>
public sealed class MachineAndSettingsShapeTests
{
    private static readonly BindingFlags DeclaredInstance =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly BindingFlags DeclaredAll =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
        BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// MAC6 (AC-E4) — <c>MachineCode.All</c> is exactly the six settled codes in the settled order
    /// B1 B2 B3 C1 C2 C3; <c>MachineCode</c> declares no grouping member (no group/line/linha), and
    /// <c>MachineRepairerAssignment</c> carries exactly the six contracted members — no grouping,
    /// no cascade, no machine-registry or per-user member.
    /// </summary>
    [Fact]
    public void MAC6_MachinesHaveNoGroupingConceptAndAssignmentsCarryOnlyTheContractedMembers()
    {
        // The six codes, in the settled order.
        Assert.Equal(
            new[] { "B1", "B2", "B3", "C1", "C2", "C3" },
            MachineCode.All.Select(machine => machine.Value));
        Assert.Equal(6, MachineCode.All.Count);

        // No grouping vocabulary anywhere on the machine code type.
        string[] grouping = ["group", "line", "linha", "machineid", "registry"];
        Assert.Empty(Offenders(DeclaredMemberNames(typeof(MachineCode)), grouping));

        // The assignment record is exactly the contracted quarter of facts plus the system facts.
        var assignment = typeof(MachineRepairerAssignment).GetProperties(DeclaredInstance)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
        Assert.Equal(
            new[]
            {
                "CreatedAt", "Machine", "MachineRepairerAssignmentId", "RepairerId",
                "UpdatedAt", "Version",
            },
            assignment.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(typeof(Guid), assignment["MachineRepairerAssignmentId"]);
        Assert.Equal(typeof(MachineCode), assignment["Machine"]);
        Assert.Equal(typeof(RepairerId), assignment["RepairerId"]);
        Assert.Equal(typeof(int), assignment["Version"]);
        Assert.Equal(typeof(DateTimeOffset), assignment["CreatedAt"]);
        Assert.Equal(typeof(DateTimeOffset), assignment["UpdatedAt"]);

        // No member of the assignment is named like a grouping, cascade, history or per-user fact.
        Assert.Empty(Offenders(DeclaredMemberNames(typeof(MachineRepairerAssignment)), grouping));
    }

    /// <summary>
    /// SET10 (AC-F8, Q-SITE) — every Definições record is a site-wide configuration row: none of
    /// <c>Repairer</c>, <c>MachineRepairerAssignment</c>, <c>PdfDirectorySettings</c>,
    /// <c>EmailList</c>, <c>EmailTemplate</c> or <c>GlassDensitySetting</c> declares a
    /// user/account member, and the two collections the contract names are present: the list
    /// carries its complete recipient set and the template its optional document type.
    /// </summary>
    [Fact]
    public void SET10_SettingsRecordsAreSiteWideWithNoPerUserDimension()
    {
        var settingsTypes = new[]
        {
            typeof(Repairer),
            typeof(MachineRepairerAssignment),
            typeof(PdfDirectorySettings),
            typeof(EmailList),
            typeof(EmailTemplate),
            typeof(GlassDensitySetting),
        };

        // Site-wide: no member on any settings record is even spelled as user/account/owner/session.
        string[] perUser = ["user", "account", "owner", "session"];
        foreach (var type in settingsTypes)
        {
            Assert.Empty(Offenders(DeclaredMemberNames(type), perUser));
        }

        // The five records settle the contracted facts only (spot shapes).
        var repairer = typeof(Repairer).GetProperties(DeclaredInstance)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
        Assert.Equal(
            new[] { "CreatedAt", "Name", "RepairerId", "UpdatedAt", "Version" },
            repairer.Keys.OrderBy(name => name, StringComparer.Ordinal));

        var pdf = typeof(PdfDirectorySettings).GetProperties(DeclaredInstance)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
        Assert.Equal(
            new[] { "BaseDirectory", "PdfDirectorySettingId", "UpdatedAt", "Version" },
            pdf.Keys.OrderBy(name => name, StringComparer.Ordinal));

        // Company-code sanity: the list carries its complete recipient set and the template its
        // optional document type — the exact members the settings surface exists for.
        var emailList = typeof(EmailList).GetProperties(DeclaredInstance)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
        Assert.Equal(typeof(IReadOnlyList<EmailRecipient>), emailList["Recipients"]);

        var emailTemplate = typeof(EmailTemplate).GetProperties(DeclaredInstance)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
        Assert.Equal(typeof(EmailTemplateDocumentType?), emailTemplate["DocumentType"]);

        // The glass-density setting carries exactly the five contracted facts (correction
        // contract §5.1): the canonical processo token, the 4-dp density, the version and the
        // timestamps — with NO tool/account/owner identity anywhere.
        var glassDensity = typeof(GlassDensitySetting).GetProperties(DeclaredInstance)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
        Assert.Equal(
            new[] { "CreatedAt", "DensityGCm3", "Processo", "UpdatedAt", "Version" },
            glassDensity.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(typeof(string), glassDensity["Processo"]);
        Assert.Equal(typeof(decimal), glassDensity["DensityGCm3"]);
        Assert.Equal(typeof(int), glassDensity["Version"]);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>Every declared member name of a type, excluding compiler-generated artifacts.</summary>
    private static IReadOnlyList<string> DeclaredMemberNames(Type type) =>
        type.GetMembers(DeclaredAll)
            .Where(member => !member.Name.Contains('<', StringComparison.Ordinal) &&
                             !member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Select(member => member.Name)
            .ToList();

    /// <summary>Every supplied name that carries any of the forbidden tokens.</summary>
    private static IReadOnlyList<string> Offenders(IEnumerable<string> names, IReadOnlyList<string> tokens) =>
        names
            .Where(name => tokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
}