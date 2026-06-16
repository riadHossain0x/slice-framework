using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MimeKit;
using Slice.Emailing;

namespace Slice.Emailing.MailKit;

public sealed class MailKitEmailOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 25;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string DefaultFrom { get; set; } = "noreply@localhost";
    /// <summary>TLS strategy. Default <see cref="SecureSocketOptions.Auto"/> negotiates based on the port.</summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.Auto;
}

/// <summary>An in-memory attachment for a MailKit email.</summary>
public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType = "application/octet-stream");

/// <summary>
/// MailKit-based <see cref="IEmailSender"/>. Beyond the SMTP sender it supports multiple recipients
/// (comma/semicolon-separated in <see cref="EmailMessage.To"/>) and file attachments. Message
/// construction is factored into the testable <see cref="BuildMessage"/>.
/// </summary>
public sealed class MailKitEmailSender(MailKitEmailOptions options) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        => SendAsync(message, attachments: [], ct);

    public async Task SendAsync(EmailMessage message, IReadOnlyCollection<EmailAttachment> attachments, CancellationToken ct = default)
    {
        var mime = BuildMessage(message, options.DefaultFrom, attachments);
        using var client = new SmtpClient();
        await client.ConnectAsync(options.Host, options.Port, options.Security, ct);
        if (!string.IsNullOrEmpty(options.UserName))
            await client.AuthenticateAsync(options.UserName, options.Password, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);
    }

    /// <summary>Builds the MIME message (recipients, body, attachments). Pure — no network — for testing.</summary>
    public static MimeMessage BuildMessage(EmailMessage message, string defaultFrom, IReadOnlyCollection<EmailAttachment> attachments)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(message.From ?? defaultFrom));
        foreach (var recipient in message.To.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            mime.To.Add(MailboxAddress.Parse(recipient));
        mime.Subject = message.Subject;

        var body = new BodyBuilder();
        if (message.IsHtml) body.HtmlBody = message.Body;
        else body.TextBody = message.Body;

        foreach (var attachment in attachments)
            body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));

        mime.Body = body.ToMessageBody();
        return mime;
    }
}

public static class MailKitEmailRegistration
{
    /// <summary>Uses MailKit (with attachment + multi-recipient support) as the email sender.</summary>
    public static IServiceCollection AddSliceMailKit(this IServiceCollection services, Action<MailKitEmailOptions> configure)
    {
        var options = new MailKitEmailOptions();
        configure(options);
        services.RemoveAll<IEmailSender>();
        services.AddSingleton(options);
        services.AddSingleton<MailKitEmailSender>();   // concrete, for the attachment-capable overload
        services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<MailKitEmailSender>());
        return services;
    }
}
