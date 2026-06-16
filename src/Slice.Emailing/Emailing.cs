using Microsoft.Extensions.Logging;
using Slice.Core.DependencyInjection;

namespace Slice.Emailing;

public sealed record EmailMessage(string To, string Subject, string Body, bool IsHtml = false, string? From = null);

/// <summary>Sends emails. Default is the no-op <see cref="NullEmailSender"/>; swap in SMTP.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>Dev/default sender — logs instead of sending.</summary>
public sealed class NullEmailSender(ILogger<NullEmailSender> logger) : IEmailSender, ISingletonDependency
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("EMAIL (not sent) → {To}: {Subject}", message.To, message.Subject);
        return Task.CompletedTask;
    }
}
