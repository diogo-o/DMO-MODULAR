using DMO.Application.Documents;

namespace DMO.UnitTests.Documents;

/// <summary>
/// Unit proofs of the SMTP transport validation of the Peso PDF email slice: the transport never
/// touches the network when its configuration is incomplete (typed <c>not-configured</c>) and
/// refuses an empty recipient set locally — no address is ever invented; the configured values
/// are the only input (no hardcoded host/account).
/// </summary>
public sealed class SmtpEmailTransportTests
{
    [Fact]
    public async Task Send_WithIncompleteConfigurationRefusesNotConfiguredWithoutNetwork()
    {
        // Missing host → not configured.
        var missingHost = new EmailTransportOptions { From = "dmo@example.com" };
        Assert.False(missingHost.IsConfigured);

        // Missing sender → not configured.
        var missingFrom = new EmailTransportOptions { Host = "smtp.example.com" };
        Assert.False(missingFrom.IsConfigured);

        // Invalid port → not configured.
        var invalidPort = new EmailTransportOptions { Host = "smtp.example.com", From = "dmo@example.com", Port = 0 };
        Assert.False(invalidPort.IsConfigured);

        // Empty options → not configured.
        Assert.False(new EmailTransportOptions().IsConfigured);

        var transport = new SmtpEmailTransport(missingHost);
        var result = await transport.SendAsync(
            new EmailMessage(["a@example.com"], "s", "b", "Peso.pdf", [1]),
            CancellationToken.None);

        Assert.Equal(EmailTransportState.NotConfigured, result.State);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task Send_WithAnEmptyRecipientSetRefusesLocally()
    {
        var transport = new SmtpEmailTransport(new EmailTransportOptions
        {
            Host = "smtp.example.com",
            From = "dmo@example.com",
        });

        var result = await transport.SendAsync(
            new EmailMessage([], "s", "b", "Peso.pdf", [1]),
            CancellationToken.None);

        Assert.Equal(EmailTransportState.Failed, result.State);
    }

    [Fact]
    public async Task Send_WithValidConfigurationIsConfigured()
    {
        var options = new EmailTransportOptions
        {
            Host = "smtp.example.com",
            From = "dmo@example.com",
        };

        Assert.True(options.IsConfigured);
        Assert.Equal(587, options.Port); // the documented default
        Assert.True(options.EnableSsl); // the documented default
    }
}