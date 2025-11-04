// FedresursScraper.Services.Models/BiddingData.cs

namespace FedresursScraper.Services.Models;

public class BiddingData
{
    /// <summary>
    /// Уникальный идентификатор (совпадает с Id торгов old.bankrot.fedresurs.ru)
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Номер торгов (на площадке)
    /// </summary>
    public string TradeNumber { get; set; } = default!;

    /// <summary>
    /// Площадка
    /// </summary>
    public string Platform { get; set; } = default!;
}
