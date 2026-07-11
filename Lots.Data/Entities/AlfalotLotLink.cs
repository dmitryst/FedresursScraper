namespace Lots.Data.Entities;

/// <summary>
/// Связка номера торгов/лота Федресурса с URL на площадке Альфалот.
/// Нужна, потому что у Альфалота нет прямого соответствия tradeNumber → URL.
/// </summary>
public class AlfalotLotLink
{
    public Guid Id { get; set; }

    /// <summary>
    /// Номер торгов как на площадке (например, "0177088").
    /// </summary>
    public string TradeNumber { get; set; } = default!;

    /// <summary>
    /// Нормализованный номер торгов (без ведущих нулей) для сопоставления с Федресурсом.
    /// </summary>
    public string TradeNumberNormalized { get; set; } = default!;

    /// <summary>
    /// Номер лота внутри торгов (например, "1").
    /// </summary>
    public string LotNumber { get; set; } = default!;

    /// <summary>
    /// Нормализованный номер лота для сопоставления.
    /// </summary>
    public string LotNumberNormalized { get; set; } = default!;

    /// <summary>
    /// Ссылка на страницу торгов (для будущего парсинга документов торгов).
    /// </summary>
    public string TradeUrl { get; set; } = default!;

    /// <summary>
    /// Ссылка на страницу лота (фото, график снижения цены).
    /// </summary>
    public string LotUrl { get; set; } = default!;

    /// <summary>
    /// Статус лота на площадке на момент индексации.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Дата окончания представления заявок (если удалось распарсить).
    /// </summary>
    public DateTime? ApplicationsEndAt { get; set; }

    /// <summary>
    /// Дата проведения / ключевая дата события (если удалось распарсить).
    /// </summary>
    public DateTime? EventAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
