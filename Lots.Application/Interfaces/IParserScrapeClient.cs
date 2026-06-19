using FedresursScraper.Services.Models;

namespace Lots.Application.Interfaces;

public interface IParserScrapeClient
{
    Task<IReadOnlyList<LotInfo>> GetLotsFromBankruptMessageAsync(
        Guid bankruptMessageId,
        CancellationToken cancellationToken = default);
}
