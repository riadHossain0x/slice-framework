# Slice.Emailing

> Email-sending abstraction for Slice with a logging no-op default and a built-in SMTP sender.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This is the core emailing abstraction for Slice. Application code depends on `IEmailSender` and sends an `EmailMessage` record; the concrete transport is a registration concern. The default registration is `NullEmailSender`, which logs the message instead of sending it (ideal for dev). A `System.Net.Mail`-based `SmtpEmailSender` is included and can be swapped in via an extension; for advanced features (attachments, multi-recipient) use the `Slice.Emailing.MailKit` adapter.

## Dependencies

- **Slice:** `Slice.Core`, `Slice.Modularity`, `Slice.Application`
- **Third-party:** `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`

## Module & registration

`SliceEmailingModule` is a `SliceModule` with `[DependsOn(typeof(SliceApplicationModule))]`. It registers `NullEmailSender` via `AddSliceConventions`. Swap in SMTP with `AddSliceSmtpEmailSender`, which removes the existing `IEmailSender`, binds `SmtpEmailOptions`, and registers `SmtpEmailSender` as a singleton.

```csharp
[DependsOn(typeof(SliceEmailingModule))]
public sealed class MyAppModule : SliceModule;

// Swap the null sender for SMTP:
services.AddSliceSmtpEmailSender(o =>
{
    o.Host = "smtp.example.com";
    o.Port = 587;
    o.EnableSsl = true;
    o.UserName = "apikey";
    o.Password = "...";
    o.DefaultFrom = "noreply@example.com";
});
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `EmailMessage` | record | `EmailMessage(string To, string Subject, string Body, bool IsHtml = false, string? From = null)`. |
| `IEmailSender` | interface | `Task SendAsync(EmailMessage message, CancellationToken ct = default)`. |
| `NullEmailSender` | class | `IEmailSender`, `ISingletonDependency`. Default — logs the message (`To` + `Subject`) instead of sending. |
| `SmtpEmailSender` | class | `IEmailSender` over `System.Net.Mail.SmtpClient`. |
| `SmtpEmailOptions` | class | `Host` (`"localhost"`), `Port` (`25`), `EnableSsl` (`true`), `UserName` (`null`), `Password` (`null`), `DefaultFrom` (`"noreply@localhost"`). |
| `SliceEmailingModule` | module | Registers the default `NullEmailSender`. |
| `SmtpEmailRegistration` | static class | Hosts `AddSliceSmtpEmailSender(this IServiceCollection, Action<SmtpEmailOptions> configure)`. |

## Usage

```csharp
public sealed class WelcomeEmail(IEmailSender email)
{
    public Task SendAsync(string to, CancellationToken ct)
        => email.SendAsync(
            new EmailMessage(
                To: to,
                Subject: "Welcome",
                Body: "<h1>Hi there</h1>",
                IsHtml: true),
            ct);
}
```

## Notes

- **Default sender:** until you call `AddSliceSmtpEmailSender` (or `AddSliceMailKit` from the MailKit package), the resolved `IEmailSender` is `NullEmailSender`, which only logs.
- **Lifetimes:** `NullEmailSender` and `SmtpEmailSender` (and `SmtpEmailOptions`) are singletons.
- **From address:** `SmtpEmailSender` uses `message.From ?? options.DefaultFrom`.
- **HTML vs text:** `EmailMessage.IsHtml` maps to `MailMessage.IsBodyHtml`.
- **Credentials:** SMTP credentials are only set when `UserName` is non-empty.
- **Single recipient:** `SmtpEmailSender` passes `message.To` straight to `MailMessage`; for comma/semicolon multi-recipient parsing and attachments, use `Slice.Emailing.MailKit`.
