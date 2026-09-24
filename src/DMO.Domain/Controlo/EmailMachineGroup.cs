using DMO.Domain.Tools;

namespace DMO.Domain.Controlo;

/// <summary>
/// The two operational machine groups of the Peso email routing: <b>B</b> (machines B1/B2/B3)
/// and <b>C</b> (machines C1/C2/C3).
/// </summary>
/// <remarks>
/// Authority: P2-T08 email slice — the machines are grouped by their leading letter, exactly two
/// groups, NO per-machine associations (B1/B2/B3/C1/C2/C3 never get individual routes): one
/// group resolves the applicable email template and its associated recipients. A machine outside
/// the set (or an unknown value) NEVER resolves a group — fail closed, nothing is guessed.
/// </remarks>
public enum EmailMachineGroup
{
    /// <summary>Machines B1/B2/B3.</summary>
    B,

    /// <summary>Machines C1/C2/C3.</summary>
    C,
}

/// <summary>
/// The exact tokens and the machine→group mapping of the two operational email groups.
/// </summary>
public static class EmailMachineGroupTokens
{
    /// <summary>The stored <c>machine_group</c> token of a group.</summary>
    public static string ToToken(EmailMachineGroup group) => group switch
    {
        EmailMachineGroup.B => "B",
        EmailMachineGroup.C => "C",
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, "Unknown email machine group."),
    };

    /// <summary>Parses a stored <c>machine_group</c> token, or <c>null</c> when not settled.</summary>
    public static EmailMachineGroup? Parse(string? token) => token?.Trim() switch
    {
        "B" => EmailMachineGroup.B,
        "C" => EmailMachineGroup.C,
        _ => null,
    };

    /// <summary>
    /// The machine → group mapping (exact): <c>B1</c>/<c>B2</c>/<c>B3</c> resolve <c>B</c>;
    /// <c>C1</c>/<c>C2</c>/<c>C3</c> resolve <c>C</c>; anything else (including an unknown machine
    /// token) is <c>null</c> — FAIL CLOSED, no guessing.
    /// </summary>
    public static EmailMachineGroup? FromMachine(MachineCode? machine) => machine?.Value switch
    {
        "B1" or "B2" or "B3" => EmailMachineGroup.B,
        "C1" or "C2" or "C3" => EmailMachineGroup.C,
        _ => null,
    };
}