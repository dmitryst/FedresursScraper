using FedresursScraper.Services.Models;

namespace Lots.Application.Interfaces;

public interface IParserScrapeClient
{
    Task<BankruptMessageScrapeResult> GetBankruptMessageDataAsync(
        Guid bankruptMessageId,
        CancellationToken cancellationToken = default);
}
