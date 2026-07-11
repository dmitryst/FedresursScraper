namespace FedresursScraper.Services;

public class AlfalotEnrichmentOptions
{
    /// <summary>
    /// Обогащение лотов (фото/график). Независимо от CatalogIndexerEnabled —
    /// можно сначала наполнить каталог, потом включить enrichment.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    public int DelayWhenNoWorkMinutes { get; set; } = 10;

    /// <summary>
    /// Индексация каталога Альфалот (purchases-all → AlfalotLotLinks).
    /// Независимо от IsEnabled.
    /// </summary>
    public bool CatalogIndexerEnabled { get; set; } = true;

    /// <summary>
    /// Интервал полного прохода по каталогу (в минутах).
    /// </summary>
    public int CatalogIndexerIntervalMinutes { get; set; } = 120;

    /// <summary>
    /// Максимум страниц каталога за один проход.
    /// </summary>
    public int CatalogMaxPages { get; set; } = 20;

    /// <summary>
    /// Сколько дней «назад» ещё считаем лот актуальным для индексации.
    /// </summary>
    public int CatalogMaxPastDays { get; set; } = 14;

    /// <summary>
    /// Базовая пауза между действиями на сайте (мс): страницы каталога, лоты.
    /// </summary>
    public int RequestDelayMs { get; set; } = 5000;

    /// <summary>
    /// Случайный разброс поверх RequestDelayMs (мс), чтобы паузы выглядели естественнее.
    /// Итоговая пауза = RequestDelayMs + Random(0..RequestDelayJitterMs).
    /// </summary>
    public int RequestDelayJitterMs { get; set; } = 4000;

    /// <summary>
    /// Пауза между скачиваниями картинок (мс).
    /// </summary>
    public int ImageDownloadDelayMs { get; set; } = 1000;

    /// <summary>
    /// Разброс паузы между картинками (мс).
    /// </summary>
    public int ImageDownloadDelayJitterMs { get; set; } = 1500;

    /// <summary>
    /// Таймаут загрузки страницы Chrome (сек).
    /// </summary>
    public int PageLoadTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Сколько ждать прохождения InProtect / появления контента (сек).
    /// </summary>
    public int WafWaitTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Пауза после старта воркера каталога перед первым запросом (сек).
    /// </summary>
    public int CatalogStartupDelaySeconds { get; set; } = 45;

    /// <summary>
    /// Пауза между пачками enrichment при наличии работы (сек, минимум).
    /// </summary>
    public int EnrichmentBatchDelaySecondsMin { get; set; } = 20;

    /// <summary>
    /// Пауза между пачками enrichment при наличии работы (сек, максимум).
    /// </summary>
    public int EnrichmentBatchDelaySecondsMax { get; set; } = 40;

    /// <summary>
    /// Сколько раз ждать появления связки в каталоге, прежде чем закрыть торги (Bidding.IsEnriched=true).
    /// Лоты без связки останутся Lot.IsEnriched=false — их будет видно.
    /// </summary>
    public int MaxNoLinkWaitAttempts { get; set; } = 2;

    /// <summary>
    /// Интервал между попытками обогащения, когда не хватает связки в каталоге (часы).
    /// </summary>
    public int NoLinkRecheckIntervalHours { get; set; } = 12;

    public TimeSpan GetPageLoadTimeout() =>
        TimeSpan.FromSeconds(Math.Max(30, PageLoadTimeoutSeconds));

    public TimeSpan GetWafWaitTimeout() =>
        TimeSpan.FromSeconds(Math.Max(30, WafWaitTimeoutSeconds));

    public int GetActionDelayMs()
    {
        var baseMs = Math.Max(2000, RequestDelayMs);
        var jitter = Math.Max(0, RequestDelayJitterMs);
        return baseMs + (jitter > 0 ? Random.Shared.Next(0, jitter + 1) : 0);
    }

    public int GetImageDelayMs()
    {
        var baseMs = Math.Max(400, ImageDownloadDelayMs);
        var jitter = Math.Max(0, ImageDownloadDelayJitterMs);
        return baseMs + (jitter > 0 ? Random.Shared.Next(0, jitter + 1) : 0);
    }

    public int GetEnrichmentBatchDelaySeconds()
    {
        var min = Math.Max(5, EnrichmentBatchDelaySecondsMin);
        var max = Math.Max(min, EnrichmentBatchDelaySecondsMax);
        return Random.Shared.Next(min, max + 1);
    }

    public int GetEffectiveMaxNoLinkWaitAttempts() =>
        MaxNoLinkWaitAttempts > 0 ? MaxNoLinkWaitAttempts : 2;

    public int GetEffectiveNoLinkRecheckIntervalHours() =>
        NoLinkRecheckIntervalHours > 0 ? NoLinkRecheckIntervalHours : 12;
}
