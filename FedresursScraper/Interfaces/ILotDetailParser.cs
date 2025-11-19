using FedresursScraper.Services.Models;

public interface ILotDetailParser
{
    Task<LotDetails> ParseDetailsAsync(Guid lotId, CancellationToken cancellationToken = default);
}