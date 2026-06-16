using Microsoft.Extensions.Logging;
using Slice.BackgroundJobs;
using Slice.Emailing;

namespace Slice.Sample.Crm.Features.CreateLead;

public sealed record WelcomeEmailArgs(Guid LeadId, string? Email);

/// <summary>Background job — runs on the worker, off the request thread; sends via IEmailSender.</summary>
public sealed class SendWelcomeEmailJob(IEmailSender email, ILogger<SendWelcomeEmailJob> logger)
    : IBackgroundJob<WelcomeEmailArgs>
{
    public async Task ExecuteAsync(WelcomeEmailArgs args, CancellationToken ct)
    {
        logger.LogInformation("JOB: sending welcome email for lead {LeadId}", args.LeadId);
        if (!string.IsNullOrWhiteSpace(args.Email))
            await email.SendAsync(new EmailMessage(args.Email, "Welcome to UAPP", "Thanks for your interest!"), ct);
    }
}
