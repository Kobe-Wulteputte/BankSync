using FluentEmail.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BS.Logic.Mailing;

public class MailSenderService(IConfiguration configuration, ILogger<MailSenderService> logger, IFluentEmail fluentEmail)
{
    public async Task SendMail(string subject, string body, string to)
    {
        logger.LogInformation($"Sending mail with subject: {subject}");

        await fluentEmail.To(to).Subject(subject).Body(body).SendAsync();
    }
}