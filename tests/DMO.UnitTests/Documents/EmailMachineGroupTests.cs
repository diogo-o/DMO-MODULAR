using DMO.Domain.Controlo;
using DMO.Domain.Tools;

namespace DMO.UnitTests.Documents;

/// <summary>
/// Unit proofs of the machine → group vocabulary of the Peso email routing (P2-T08 email slice):
/// B1/B2/B3 resolve B; C1/C2/C3 resolve C; ANYTHING else (unknown token, other letters, empty)
/// resolves <c>null</c> — fail closed, no guessing and NO per-machine associations.
/// </summary>
public sealed class EmailMachineGroupTests
{
    [Theory]
    [InlineData("B1")]
    [InlineData("B2")]
    [InlineData("B3")]
    public void FromMachine_BMachinesResolveGroupB(string machine)
    {
        Assert.Equal(EmailMachineGroup.B, EmailMachineGroupTokens.FromMachine(MachineCode.Parse(machine)));
    }

    [Theory]
    [InlineData("C1")]
    [InlineData("C2")]
    [InlineData("C3")]
    public void FromMachine_CMachinesResolveGroupC(string machine)
    {
        Assert.Equal(EmailMachineGroup.C, EmailMachineGroupTokens.FromMachine(MachineCode.Parse(machine)));
    }

    [Theory]
    [InlineData("X1")]
    [InlineData("A1")]
    [InlineData("B0")]
    [InlineData("C4")]
    [InlineData("")]
    public void FromMachine_MachinesOutsideTheGroupsFailClosed(string machine)
    {
        Assert.Null(EmailMachineGroupTokens.FromMachine(MachineCode.Parse(machine)));
    }

    [Fact]
    public void FromMachine_AnUnknownTokenIsNeverGuessed()
    {
        Assert.Null(EmailMachineGroupTokens.FromMachine(MachineCode.From("NAO-EXISTE")));
        Assert.Null(EmailMachineGroupTokens.FromMachine(null));
    }

    [Fact]
    public void Tokens_RoundTripExactlyBAndC()
    {
        Assert.Equal("B", EmailMachineGroupTokens.ToToken(EmailMachineGroup.B));
        Assert.Equal("C", EmailMachineGroupTokens.ToToken(EmailMachineGroup.C));
        Assert.Equal(EmailMachineGroup.B, EmailMachineGroupTokens.Parse("B"));
        Assert.Equal(EmailMachineGroup.C, EmailMachineGroupTokens.Parse("C"));
        Assert.Null(EmailMachineGroupTokens.Parse("A"));
        Assert.Null(EmailMachineGroupTokens.Parse(null));
    }
}