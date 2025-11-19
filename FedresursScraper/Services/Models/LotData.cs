// FedresursScraper.Services.Models/LotData.cs

namespace FedresursScraper.Services.Models;

public class LotData
{
    /// <summary>
    /// Уникальный идентификатор (совпадает с Id торгов old.bankrot.fedresurs.ru)
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Номер торгов (на площадке)
    /// </summary>
    public string LotNumber { get; set; } = default!;

    /// <summary>
    /// Площадка
    /// </summary>
    public string Platform { get; set; } = default!;
}
