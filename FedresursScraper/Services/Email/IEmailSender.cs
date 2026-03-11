namespace FedresursScraper.Services.Email;

public interface IEmailSender
{
    Task SendEmailAsync(string toEmail, string subject, string message, CancellationToken cancellationToken = default);
}
