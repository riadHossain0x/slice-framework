using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MailKit.Security;
using Slice.Emailing;
using Slice.Emailing.MailKit;

namespace Slice.Emailing.MailKit.Tests;

/// <summary>
/// Sends a real email through the MailKit sender to a Mailpit SMTP server (Testcontainers) and
/// verifies receipt via Mailpit's HTTP API — end-to-end proof the sender talks SMTP correctly.
/// </summary>
public sealed class MailKitSendTests : IAsyncLifetime
{
    private readonly IContainer _mailpit = new ContainerBuilder()
        .WithImage("axllent/mailpit:latest")
        .WithPortBinding(1025, true)   // SMTP
        .WithPortBinding(8025, true)   // HTTP API
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/")))
        .Build();

    public Task InitializeAsync() => _mailpit.StartAsync();
    public Task DisposeAsync() => _mailpit.DisposeAsync().AsTask();

    [Fact]
    public async Task Email_sent_via_MailKit_is_received_by_the_SMTP_server()
    {
        var sender = new MailKitEmailSender(new MailKitEmailOptions
        {
            Host = _mailpit.Hostname,
            Port = _mailpit.GetMappedPublicPort(1025),
            Security = SecureSocketOptions.None,
            DefaultFrom = "noreply@slice.test"
        });

        await sender.SendAsync(new EmailMessage(
            To: "dest@slice.test",
            Subject: "Integration Subject",
            Body: "body-over-smtp"));

        using var http = new HttpClient { BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}") };
        using var doc = JsonDocument.Parse(await http.GetStringAsync("/api/v1/messages"));

        var messages = doc.RootElement.GetProperty("messages");
        Assert.True(messages.GetArrayLength() >= 1);
        Assert.Equal("Integration Subject", messages[0].GetProperty("Subject").GetString());
    }
}
