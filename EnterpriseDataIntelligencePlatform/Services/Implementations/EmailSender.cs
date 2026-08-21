
using EnterpriseDataIntelligencePlatform.Services.Interfaces;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class EmailSender(ILogger<EmailSender> logger) : IEmailSender
{
    public Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Development email generated for {Recipient}. Subject: {Subject}. Body: {Body}",
            recipient,
            subject,
            body);

        return Task.CompletedTask;
    }
}
