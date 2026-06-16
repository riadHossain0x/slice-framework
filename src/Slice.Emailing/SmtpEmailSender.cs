using System.Net;
using System.Net.Mail;

namespace Slice.Emailing;

public sealed class SmtpEmailOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 25;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string DefaultFrom { get; set; } = "noreply@localhost";
}

public sealed class SmtpEmailSender(SmtpEmailOptions options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = options.EnableSsl };
        if (!string.IsNullOrEmpty(options.UserName))
            client.Credentials = new NetworkCredential(options.UserName, options.Password);

        using var mail = new MailMessage(message.From ?? options.DefaultFrom, message.To, message.Subject, message.Body)
        {
            IsBodyHtml = message.IsHtml
        };
        await client.SendMailAsync(mail, ct);
    }
}
