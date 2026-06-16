# Slice.Emailing.MailKit

> MailKit-based email sender for Slice with attachments, multi-recipient parsing, and a pure testable message builder.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This adapter implements `Slice.Emailing.IEmailSender` using MailKit/MimeKit. Beyond the plain SMTP sender it adds file attachments and multiple recipients (comma/semicolon-separated in `EmailMessage.To`), and exposes configurable TLS via MailKit's `SecureSocketOptions`. MIME construction is factored into a static, network-free `BuildMessage` method so it can be unit-tested directly. Verified with a real Mailpit SMTP container.

## Dependencies

- **Slice:** `Slice.Emailing`
- **Third-party:** `MailKit` (+ MimeKit), `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

No `SliceModule` of its own — registration is via the `AddSliceMailKit` extension. It binds `MailKitEmailOptions`, removes any existing `IEmailSender`, registers the concrete `MailKitEmailSender` as a singleton (so the attachment-capable overload is injectable), and aliases `IEmailSender` to it.

```csharp
using MailKit.Security;

services.AddSliceMailKit(o =>
{
    o.Host = "smtp.example.com";
    o.Port = 587;
    o.UserName = "apikey";
    o.Password = "...";
    o.DefaultFrom = "noreply@example.com";
    o.Security = SecureSocketOptions.StartTls;
});
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `MailKitEmailSender` | class | `IEmailSender`. Adds `SendAsync(EmailMessage message, IReadOnlyCollection<EmailAttachment> attachments, CancellationToken ct = default)` plus the base `SendAsync(EmailMessage, CancellationToken)` (calls the overload with no attachments). |
| `MailKitEmailOptions` | class | `Host` (`"localhost"`), `Port` (`25`), `UserName` (`null`), `Password` (`null`), `DefaultFrom` (`"noreply@localhost"`), `Security` (`SecureSocketOptions.Auto`). |
| `EmailAttachment` | record | `EmailAttachment(string FileName, byte[] Content, string ContentType = "application/octet-stream")`. |
| `MailKitEmailRegistration` | static class | Hosts `AddSliceMailKit(this IServiceCollection, Action<MailKitEmailOptions> configure)`. |
| `MailKitEmailSender.BuildMessage` | static method | `MimeMessage BuildMessage(EmailMessage message, string defaultFrom, IReadOnlyCollection<EmailAttachment> attachments)` — pure, no network; for testing. |

## Usage

Send with attachments and multiple recipients:

```csharp
public sealed class ReportMailer(MailKitEmailSender sender)
{
    public Task SendAsync(byte[] pdf, CancellationToken ct)
    {
        var message = new EmailMessage(
            To: "alice@example.com, bob@example.com; carol@example.com",
            Subject: "Monthly report",
            Body: "<p>See attached.</p>",
            IsHtml: true);

        var attachments = new[]
        {
            new EmailAttachment("report.pdf", pdf, "application/pdf")
        };

        return sender.SendAsync(message, attachments, ct);
    }
}
```

Backend-agnostic code can still depend on `IEmailSender`:

```csharp
public sealed class Notifier(IEmailSender email)
{
    public Task PingAsync(CancellationToken ct)
        => email.SendAsync(new EmailMessage("ops@example.com", "Ping", "ok"), ct);
}
```

Unit-test the MIME output without a server:

```csharp
var mime = MailKitEmailSender.BuildMessage(
    new EmailMessage("a@x.com;b@x.com", "Hi", "body"),
    defaultFrom: "noreply@x.com",
    attachments: []);

// mime.To has two recipients; mime.From is noreply@x.com.
```

## Notes

- **Attachments require the concrete type:** inject `MailKitEmailSender` (not `IEmailSender`) to call the attachment overload — `AddSliceMailKit` registers the concrete class for exactly this reason.
- **Multi-recipient parsing:** `EmailMessage.To` is split on `,` and `;` with empty entries removed and entries trimmed; each is added via `MailboxAddress.Parse`.
- **From address:** uses `message.From ?? options.DefaultFrom`.
- **HTML vs text:** `EmailMessage.IsHtml` selects `BodyBuilder.HtmlBody` vs `TextBody`.
- **TLS:** `MailKitEmailOptions.Security` (default `SecureSocketOptions.Auto`) is passed to `ConnectAsync`; authentication runs only when `UserName` is non-empty. The client connects, optionally authenticates, sends, and disconnects with `quit: true` per call.
- **`BuildMessage` is pure:** it builds the `MimeMessage` with no I/O, making recipient/body/attachment logic directly testable.
- **Lifetimes:** `MailKitEmailSender` and `MailKitEmailOptions` are singletons.
