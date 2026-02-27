using System.ComponentModel.DataAnnotations;
using NpgsqlTypes;

namespace Lots.Data.Entities;

public class Lot
{
    /// <summary>
    /// Внутренний уникальный идентификатор лота
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Глобальный сквозной номер лота для URL и поиска
    /// </summary>
    public int PublicId { get; set; }

    /// <summary>
    /// Номер лота внутри торгов (указан в торгах Федресурса)
    /// </summary>
    public string? LotNumber { get; set; }

    public decimal? StartPrice { get; set; }
    public decimal? Step { get; set; }
    public decimal? Deposit { get; set; }
    public string? Description { get; set; }
    public string? Title { get; set; }

    // <summary>
    /// URL-friendly название лота. Используется для формирования SEO-ссылок.
    /// </summary>
    [MaxLength(200)] // С запасом, хотя мы режем до 60
    public string? Slug { get; set; }

    public bool IsSharedOwnership { get; set; }
    public string? ViewingProcedure { get; set; }
    public List<LotCategory> Categories { get; set; } = new();
    public List<LotCadastralNumber> CadastralNumbers { get; set; } = new();
    public ICollection<LotImage> Images { get; set; } = new List<LotImage>();
    public ICollection<LotDocument> Documents { get; set; } = new List<LotDocument>();
    public ICollection<LotPriceSchedule> PriceSchedules { get; set; } = new List<LotPriceSchedule>();
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>
    /// Данные из Росреестра по кадастровым номерам, входящим в лот
    /// </summary>
    public ICollection<CadastralInfo> CadastralInfos { get; set; } = new List<CadastralInfo>();

    public DateTime CreatedAt { get; set; }
    public Guid BiddingId { get; set; }
    public Bidding Bidding { get; set; } = default!;

    /// <summary>
    /// Код региона местонахождения имущества (первые две цифры ИНН должника)
    /// </summary>
    public string? PropertyRegionCode { get; set; }

    /// <summary>
    /// Название региона местонахождения имущества
    /// </summary>
    public string? PropertyRegionName { get; set; }

    /// <summary>
    /// Полный адрес местонахождения имущества (если указан в описании)
    /// </summary>
    public string? PropertyFullAddress { get; set; }

    /// <summary>
    /// Рыночная стоимость объекта (оценка ИИ)
    /// </summary>
    public decimal? MarketValue { get; set; }

    /// <summary>
    /// Нижняя граница рыночной стоимости (ликвидационная цена / быстрая продажа).
    /// </summary>
    public decimal? MarketValueMin { get; set; }

    /// <summary>
    /// Верхняя граница рыночной стоимости (оптимистичная / рыночная цена).
    /// </summary>
    public decimal? MarketValueMax { get; set; }

    /// <summary>
    /// Уровень уверенности модели в оценке: "low", "medium", "high".
    /// </summary>
    [MaxLength(20)]
    public string? PriceConfidence { get; set; }

    /// <summary>
    /// Короткий инвестиционный комментарий (2–3 предложения): логика marketValue, риски, потенциал.
    /// </summary>
    public string? InvestmentSummary { get; set; }

    /// <summary>
    /// Статус торгов по лоту (old.bankrot.fedresurs.ru), например:
    /// "Завершенные", "Торги отменены", "Торги не состоялись", "Открыт прием заявок".
    /// </summary>
    public string? TradeStatus { get; set; }

    /// <summary>
    /// Итоговая/текущая цена по лоту (old.bankrot.fedresurs.ru).
    /// </summary>
    public decimal? FinalPrice { get; set; }

    /// <summary>
    /// Победитель торгов по лоту (если есть).
    /// </summary>
    public string? WinnerName { get; set; }

    /// <summary>
    /// ИНН победителя торгов по лоту (если есть).
    /// </summary>
    public string? WinnerInn { get; set; }

    // Техническое поле для хранения поискового индекса
    public NpgsqlTsVector SearchVector { get; set; } = default!;

    // Domain Logic

    /// <summary>
    /// Массив всех возможных конечных статусов лота.
    /// Вынесено в доменную модель согласно принципам DDD.
    /// </summary>
    public static readonly string[] FinalTradeStatuses =
    {
        "Завершенные",
        "Торги отменены",
        "Торги не состоялись",
        "Торги завершены (нет данных)"
    };

    /// <summary>
    /// Возвращает true, если лот все еще актуален (торги не завершены и не отменены).
    /// </summary>
    public bool IsActive()
    {
        if (string.IsNullOrWhiteSpace(TradeStatus))
            return true;

        // Если статус содержится в списке финальных, значит лот НЕ активен
        return !FinalTradeStatuses.Contains(TradeStatus, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Добавляет информацию из Росреестра, защищая от дубликатов.
    /// </summary>
    public void AddCadastralInfo(CadastralInfo newInfo)
    {
        ArgumentNullException.ThrowIfNull(newInfo);

        // Защита инварианта: проверка на дубликат внутри самого агрегата
        bool isDuplicate = CadastralInfos.Any(c => c.CadastralNumber == newInfo.CadastralNumber);

        if (!isDuplicate)
        {
            // EF Core автоматически проставит LotId при сохранении, 
            // но для консистентности объекта в памяти мы можем задать его явно
            newInfo.LotId = this.Id;
            CadastralInfos.Add(newInfo);
        }
    }

    /// <summary>
    /// Устанавливает координаты для совместимости с Яндекс.Картами, 
    /// только если они еще не были заданы.
    /// </summary>
    public void SetCoordinatesIfEmpty(double lat, double lon)
    {
        if (!Latitude.HasValue && !Longitude.HasValue)
        {
            Latitude = lat;
            Longitude = lon;
        }
    }

    /// <summary>
    /// Пытается перевести лот в статус "Торги не состоялись" или "Торги отменены".
    /// Используется для дообогащения данных с площадки, если Федресурс задерживает публикацию результатов торгов.
    /// </summary>
    public bool TryMarkAsFailedOrCancelled(string newStatus, string source, out LotAuditEvent? auditEvent)
    {
        auditEvent = null;
        var normalizedStatus = newStatus?.Trim();

        // Защита инварианта: разрешаем только эти два статуса
        if (normalizedStatus != "Торги не состоялись" && normalizedStatus != "Торги отменены")
        {
            return false;
        }

        // Если статус уже такой, ничего не делаем
        if (string.Equals(TradeStatus, normalizedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var oldStatus = TradeStatus;

        // Обновляем состояние
        TradeStatus = normalizedStatus;
        FinalPrice = null;
        WinnerName = null;
        WinnerInn = null;

        // Генерируем событие аудита прямо в доменной модели
        auditEvent = new LotAuditEvent
        {
            LotId = this.Id,
            EventType = "TradeStatusEnrichment",
            Status = "Success",
            Source = source,
            Timestamp = DateTime.UtcNow,
            Details = $"Статус уточнен с ЭТП. Предыдущий: '{oldStatus}'. Новый: '{normalizedStatus}'."
        };

        return true;
    }
}