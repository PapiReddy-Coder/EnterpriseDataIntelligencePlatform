
namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default);
}
