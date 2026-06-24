using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities;

/// <summary>
/// Торги
/// </summary>
public class Bidding
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

    /// <summary>
    /// Дата объявления о торгах
    /// </summary>
    public DateTime? AnnouncedAt { get; set; }

    /// <summary>
    /// Вид торгов
    /// </summary>
    public string Type { get; set; } = default!;

    /// <summary>
    /// Период приема заявок
    /// </summary>
    public string? BidAcceptancePeriod { get; set; }

    /// <summary>
    /// Период торгов
    /// </summary>
    public string? TradePeriod { get; set; }

    /// <summary>
    /// Дата объявления результатов
    /// </summary>
    public DateTime? ResultsAnnouncementDate { get; set; }

    /// <summary>
    /// Организатор торгов
    /// </summary>
    public string? Organizer { get; set; }

    /// <summary>
    /// Должник
    /// </summary>
    public Guid? DebtorId { get; set; }
    [ForeignKey("DebtorId")]
    public Subject? Debtor { get; set; }

    /// <summary>
    /// Арбитражный управляющий
    /// </summary>
    public Guid? ArbitrationManagerId { get; set; }
    [ForeignKey("ArbitrationManagerId")]
    public Subject? ArbitrationManager { get; set; }

    /// <summary>
    /// Судебное дело
    /// </summary>
    public Guid? LegalCaseId { get; set; }
    [ForeignKey("LegalCaseId")]
    public LegalCase? LegalCase { get; set; }

    /// <summary>
    /// Идентификатор сообщения о торгах
    /// </summary>
    public Guid BankruptMessageId { get; set; }

    /// <summary>
    /// Порядок ознакомления с имуществом
    /// </summary>
    public string? ViewingProcedure { get; set; }

    /// <summary>
    /// Дата сохранения в БД
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Лот был обогащен (с сайта площадки)
    /// </summary>
    public bool? IsEnriched { get; set; }

    /// <summary>
    /// Дата обогащения лота
    /// </summary>
    public DateTime? EnrichedAt { get; set; }

    /// <summary>
    /// Лоты торгов
    /// </summary>
    public List<Lot> Lots { get; set; } = new();

    /// <summary>
    /// Стейт обогощения торгов (со страницы площадки)
    /// </summary>
    public virtual EnrichmentState? EnrichmentState { get; set; }

    /// <summary>
    /// Признак того, что статусы по всем лотам этих торгов были обновлены до конечного 
    /// ("Завершенные", "Торги отменены", "Торги не состоялись").
    /// </summary>
    public bool IsTradeStatusesFinalized { get; set; }

    /// <summary>
    /// Дата и время последней проверки статуса торгов фоновым сервисом
    /// </summary>
    public DateTime? LastStatusCheckAt { get; set; }

    /// <summary>
    /// Запланированные дата и время следующей проверки статусов торгов.
    /// Позволяет отложить бессмысленные проверки, если торги еще не начались или идут.
    /// </summary>
    public DateTime? NextStatusCheckAt { get; set; }

    /// <summary>
    /// Флаг: указывает, что торги были обработаны, но лотов на площадке реально нет
    /// </summary>
    public bool HasNoLots { get; set; } = false;

    // Domain Logic

    // <summary>
    /// Вычисляет и устанавливает дату следующей проверки статусов торгов
    /// на основе типа торгов и их временных рамок.
    /// </summary>
    public void ScheduleNextCheck(DateTime now) =>
        ScheduleNextCheck(now, suspendedRecheckDays: 0, useSuspendedInterval: false);

    /// <summary>
    /// Планирует следующую проверку. Для приостановленных торгов можно задать отдельный интервал.
    /// </summary>
    public void ScheduleNextCheck(DateTime now, int suspendedRecheckDays, bool useSuspendedInterval)
    {
        var utcNow = now.Kind == DateTimeKind.Utc ? now : now.ToUniversalTime();

        if (useSuspendedInterval && suspendedRecheckDays > 0)
        {
            NextStatusCheckAt = utcNow.AddDays(suspendedRecheckDays);
            return;
        }

        var typeStr = Type?.ToLower() ?? "";
        bool isPublicOffer = typeStr.Contains("публичное предложение");

        if (isPublicOffer)
        {
            // Прибавляем 7 дней к текущей NextStatusCheckAt (или к текущему времени)
            var baseDate = NextStatusCheckAt ?? utcNow;
            var proposedDate = baseDate.AddDays(7);

            // NextStatusCheckAt не может быть раньше старта приема заявок
            var startAcceptanceDate = TryParsePeriodStart(BidAcceptancePeriod);
            if (startAcceptanceDate.HasValue && proposedDate < startAcceptanceDate.Value)
            {
                proposedDate = startAcceptanceDate.Value.AddDays(7);
            }

            // Дата проверки не может быть в прошлом.
            // Если после всех вычислений дата оказалась раньше текущего времени, 
            // прибавляем 7 дней строго к текущей дате (utcNow).
            if (proposedDate < utcNow)
            {
                NextStatusCheckAt = utcNow.AddDays(7);
            }
            else
            {
                NextStatusCheckAt = proposedDate;
            }
        }
        else
        {
            // Для аукционов:
            DateTime? proposedDate = null;

            // Смотрим на ResultsAnnouncementDate
            if (ResultsAnnouncementDate.HasValue)
            {
                var resultsDateUtc = ResultsAnnouncementDate.Value.Kind == DateTimeKind.Utc
                    ? ResultsAnnouncementDate.Value
                    : ResultsAnnouncementDate.Value.ToUniversalTime();

                proposedDate = resultsDateUtc.AddDays(3); // Прибавляем 3 дня
            }
            else
            {
                // Если нет ResultsAnnouncementDate, смотрим на TradePeriod
                var tradePeriodEnd = TryParsePeriodEnd(TradePeriod);
                if (tradePeriodEnd.HasValue)
                {
                    proposedDate = tradePeriodEnd.Value.AddDays(3); // Прибавляем 3 дня как буфер
                }
                else
                {
                    // Если TradePeriod пусто или равно "нет данных", смотрим на BidAcceptancePeriod
                    var bidAcceptanceEnd = TryParsePeriodEnd(BidAcceptancePeriod);
                    if (bidAcceptanceEnd.HasValue)
                    {
                        proposedDate = bidAcceptanceEnd.Value.AddDays(5);
                    }
                }
            }

            if (proposedDate.HasValue)
            {
                // Защита от бесконечного цикла: если расчетная дата окончания периода 
                // уже давно в прошлом, планируем следующую проверку отталкиваясь от сегодня
                if (proposedDate.Value < utcNow)
                {
                    NextStatusCheckAt = utcNow.AddDays(2);
                }
                else
                {
                    NextStatusCheckAt = proposedDate;
                }
            }
            else
            {
                // Fallback, если совсем никаких дат нет
                NextStatusCheckAt = utcNow.AddDays(7);
            }
        }
    }

    /// <summary>
    /// Проверяет, превышен ли лимит времени ожидания результатов торгов.
    /// </summary>
    public bool IsExpired(DateTime now, TimeSpan timeout)
    {
        var utcNow = now.Kind == DateTimeKind.Utc ? now : now.ToUniversalTime();

        // Проверяем по дате объявления результатов
        if (ResultsAnnouncementDate.HasValue)
        {
            var resultsDateUtc = ResultsAnnouncementDate.Value.Kind == DateTimeKind.Utc
                ? ResultsAnnouncementDate.Value
                : ResultsAnnouncementDate.Value.ToUniversalTime();

            return (utcNow - resultsDateUtc) > timeout;
        }

        // Проверяем по дате окончания приема заявок
        var endAcceptanceDate = TryParsePeriodEnd(BidAcceptancePeriod);
        if (endAcceptanceDate.HasValue)
        {
            return (utcNow - endAcceptanceDate.Value) > timeout;
        }

        // Fallback: если дат нет, отсчитываем от даты создания или объявления
        var fallbackDate = AnnouncedAt ?? CreatedAt;
        var fallbackDateUtc = fallbackDate.Kind == DateTimeKind.Utc ? fallbackDate : fallbackDate.ToUniversalTime();

        return (utcNow - fallbackDateUtc) > timeout;
    }

    /// <summary>
    /// Инвалидирует оставшиеся активные лоты и переводит торги в финальный статус.
    /// </summary>
    public List<Lot> ForceFinalizeMissingResults(string source, out List<LotAuditEvent> auditEvents)
    {
        auditEvents = [];
        var changedLots = new List<Lot>();

        // Если торги уже помечены как финальные, повторная обработка не требуется.
        if (IsTradeStatusesFinalized)
        {
            return changedLots;
        }

        foreach (var lot in Lots.Where(l => l.IsActive()))
        {
            if (lot.TryMarkAsFinalizedWithoutData(source, out var auditEvent))
            {
                if (auditEvent != null)
                {
                    auditEvents.Add(auditEvent);
                }

                changedLots.Add(lot);
            }
        }

        // Переводим агрегат в конечное состояние
        IsTradeStatusesFinalized = true;
        NextStatusCheckAt = null;

        return changedLots;
    }

    private DateTime? TryParsePeriodStart(string? period)
    {
        if (string.IsNullOrWhiteSpace(period)) return null;

        var parts = period.Split(new[] { '-', '—', '–' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            var startStr = parts[0].Trim();
            if (DateTime.TryParseExact(startStr, "dd.MM.yyyy HH:mm",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var startDate))
            {
                return DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
            }
        }
        return null;
    }

    private DateTime? TryParsePeriodEnd(string? period)
    {
        if (string.IsNullOrWhiteSpace(period)) return null;

        var parts = period.Split(new[] { '-', '—', '–' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 1)
        {
            var endStr = parts[parts.Length - 1].Trim();

            // Если дата неизвестна
            if (endStr.Equals("нет данных", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (DateTime.TryParseExact(endStr, "dd.MM.yyyy HH:mm",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var endDate))
            {
                return DateTime.SpecifyKind(endDate, DateTimeKind.Utc);
            }
        }
        return null;
    }
}