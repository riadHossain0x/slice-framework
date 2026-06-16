using System.Text;
using MimeKit;
using Slice.Emailing;
using Slice.Emailing.MailKit;

namespace Slice.Emailing.MailKit.Tests;

/// <summary>Pure unit tests for MIME construction — no SMTP server needed.</summary>
public sealed class MailKitMessageTests
{
    [Fact]
    public void BuildMessage_maps_multiple_recipients_html_body_and_attachments()
    {
        var message = new EmailMessage(
            To: "a@x.com, b@x.com; c@x.com",
            Subject: "Hello",
            Body: "<b>hi</b>",
            IsHtml: true,
            From: "sender@x.com");

        var attachments = new[] { new EmailAttachment("note.txt", Encoding.UTF8.GetBytes("file-body"), "text/plain") };

        var mime = MailKitEmailSender.BuildMessage(message, "default@x.com", attachments);

        Assert.Equal("sender@x.com", ((MailboxAddress)mime.From[0]).Address);
        Assert.Equal(3, mime.To.Count);                         // comma + semicolon split
        Assert.Equal("Hello", mime.Subject);
        Assert.Contains("<b>hi</b>", mime.HtmlBody);            // HTML body set
        Assert.Contains(mime.Attachments, a => a.ContentDisposition?.FileName == "note.txt");
    }

    [Fact]
    public void BuildMessage_falls_back_to_default_from_and_text_body()
    {
        var message = new EmailMessage(To: "only@x.com", Subject: "S", Body: "plain text");
        var mime = MailKitEmailSender.BuildMessage(message, "default@x.com", []);

        Assert.Equal("default@x.com", ((MailboxAddress)mime.From[0]).Address);   // no From → default
        Assert.Equal("plain text", mime.TextBody);
        Assert.Null(mime.HtmlBody);
    }
}
