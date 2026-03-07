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

    public async Task SendEmailAsync(string toEmail, string subject, string body)
    {
        ValidateOptions();

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
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

        await client.SendMailAsync(message);
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
}
