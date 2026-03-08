using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace RLRentalApp.Web.Services;

public class GmailEmailService : IEmailService
{
    private readonly GmailSmtpOptions _options;

    public GmailEmailService(IOptions<GmailSmtpOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendEmailAsync(
        string toEmail,
        string subject,
        string body,
        byte[]? attachmentBytes = null,
        string? attachmentFileName = null,
        string? attachmentContentType = null)
    {
        ValidateOptions();

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_options.Username, _options.AppPassword)
        };

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromDisplayName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };

        message.To.Add(toEmail);

        if (attachmentBytes is not null && attachmentBytes.Length > 0)
        {
            var stream = new MemoryStream(attachmentBytes);
            var attachmentName = string.IsNullOrWhiteSpace(attachmentFileName) ? "statement.pdf" : attachmentFileName;
            var mediaType = string.IsNullOrWhiteSpace(attachmentContentType) ? "application/pdf" : attachmentContentType;
            var attachment = new Attachment(stream, attachmentName, mediaType);
            message.Attachments.Add(attachment);
        }

        try
        {
            await client.SendMailAsync(message);
        }
        catch (SmtpException ex) when (IsGmailAuthenticationFailure(ex))
        {
            throw new InvalidOperationException(
                "Gmail SMTP authentication failed (5.7.0). " +
                "Use a Google App Password (not your normal password), ensure 2-Step Verification is enabled, " +
                "and keep GmailSmtp:FromEmail the same as GmailSmtp:Username unless Send As is configured in Gmail.",
                ex);
        }
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.Host)
            || _options.Port <= 0
            || string.IsNullOrWhiteSpace(_options.Username)
            || string.IsNullOrWhiteSpace(_options.AppPassword)
            || string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            throw new InvalidOperationException("Gmail SMTP settings are missing. Update GmailSmtp configuration in appsettings.");
        }
    }

    private static bool IsGmailAuthenticationFailure(SmtpException ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("5.7.0", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Authentication Required", StringComparison.OrdinalIgnoreCase)
               || message.Contains("client was not authenticated", StringComparison.OrdinalIgnoreCase);
    }
}
