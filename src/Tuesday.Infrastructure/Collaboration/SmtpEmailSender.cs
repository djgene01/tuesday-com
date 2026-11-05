using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tuesday.Application.Collaboration;

namespace Tuesday.Infrastructure.Collaboration;

/// <summary>
/// SMTP when it is configured, a log line otherwise. Never throws at the caller: a failed
/// email must not roll back the write that caused it.
/// </summary>
public sealed class SmtpEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            logger.LogInformation("Email suppressed (disabled): {Subject} -> {To}", subject, to);
            return;
        }

        using var message = new MailMessage(settings.From, to, subject, htmlBody)
        {
            IsBodyHtml = true
        };

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.UseStartTls
        };

        if (!string.IsNullOrWhiteSpace(settings.User))
        {
            client.Credentials = new NetworkCredential(settings.User, settings.Password);
        }

        await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
    }
}
