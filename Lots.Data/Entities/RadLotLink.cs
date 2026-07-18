namespace Lots.Data.Entities;

/// <summary>
/// Связка «Идентификатор лота в ЕФРСБ» + номер лота с URL на lot-online (РАД).
/// Нужна, потому что у РАД нет прямого соответствия tradeNumber → URL.
/// </summary>
public class RadLotLink
{
    public Guid Id { get; set; }

    /// <summary>
    /// Идентификатор лота в ЕФРСБ с площадки РАД (общий для всех лотов одних торгов).
    /// Сопоставляется с <see cref="Bidding.TradeNumber"/>.
    /// </summary>
    public string EfrsbLotId { get; set; } = default!;

    /// <summary>
    /// Нормализованный идентификатор ЕФРСБ (цифры без ведущих нулей).
    /// </summary>
    public string EfrsbLotIdNormalized { get; set; } = default!;

    /// <summary>
    /// Номер лота внутри торгов (например, "1", "11").
    /// </summary>
    public string LotNumber { get; set; } = default!;

    /// <summary>
    /// Нормализованный номер лота для сопоставления.
    /// </summary>
    public string LotNumberNormalized { get; set; } = default!;

    /// <summary>
    /// product_id на catalog.lot-online.ru.
    /// </summary>
    public long ProductId { get; set; }

    /// <summary>
    /// Код лота на РАД (например, "РАД-453943").
    /// </summary>
    public string? LotCode { get; set; }

    /// <summary>
    /// Ссылка на страницу лота (фото, график снижения цены).
    /// </summary>
    public string LotUrl { get; set; } = default!;

    /// <summary>
    /// Статус лота на площадке на момент индексации.
    /// </summary>
    public string? Status { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
