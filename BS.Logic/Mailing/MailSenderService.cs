using FluentEmail.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BS.Logic.Mailing;

public class MailSenderService(IConfiguration configuration, ILogger<MailSenderService> logger, IFluentEmail fluentEmail)
{
    /// <summary>
    /// Sends mail. <paramref name="isHtml"/> defaults to false because FluentEmail does too, and
    /// existing callers send plain text — passing HTML without it shows the markup as literal text.
    /// </summary>
    public async Task SendMail(string subject, string body, string to, bool isHtml = false, string? plainTextAlternative = null)
    {
        logger.LogInformation($"Sending mail with subject: {subject}");

        var email = fluentEmail.To(to).Subject(subject).Body(body, isHtml);

        if (isHtml && !string.IsNullOrWhiteSpace(plainTextAlternative))
        {
            email = email.PlaintextAlternativeBody(plainTextAlternative);
        }

        await email.SendAsync();
    }
}