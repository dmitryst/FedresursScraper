namespace FedresursScraper.Services;

public class RadEnrichmentOptions
{
    /// <summary>
    /// Обогащение лотов (фото/график). Независимо от CatalogIndexerEnabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    public int DelayWhenNoWorkMinutes { get; set; } = 10;

    /// <summary>
    /// Индексация каталога РАД (имущество должников → RadLotLinks).
    /// </summary>
    public bool CatalogIndexerEnabled { get; set; } = true;

    /// <summary>
    /// Интервал полного прохода по каталогу (в минутах).
    /// </summary>
    public int CatalogIndexerIntervalMinutes { get; set; } = 120;

    /// <summary>
    /// Максимум страниц каталога за один проход.
    /// </summary>
    public int CatalogMaxPages { get; set; } = 30;

    /// <summary>
    /// Сколько страниц подряд без новых вставок — останавливаем инкрементальный обход.
    /// </summary>
    public int CatalogStopAfterPagesWithoutNew { get; set; } = 1;

    /// <summary>
    /// Раз в столько часов делаем полный проход до CatalogMaxPages (без early-stop).
    /// </summary>
    public int CatalogFullRescanIntervalHours { get; set; } = 24;

    /// <summary>
    /// Базовая пауза между HTTP-запросами (мс).
    /// </summary>
    public int RequestDelayMs { get; set; } = 2000;

    /// <summary>
    /// Случайный разброс поверх RequestDelayMs (мс).
    /// </summary>
    public int RequestDelayJitterMs { get; set; } = 1500;

    /// <summary>
    /// Пауза между скачиваниями картинок (мс).
    /// </summary>
    public int ImageDownloadDelayMs { get; set; } = 800;

    /// <summary>
    /// Разброс паузы между картинками (мс).
    /// </summary>
    public int ImageDownloadDelayJitterMs { get; set; } = 1000;

    /// <summary>
    /// Пауза после старта воркера каталога перед первым запросом (сек).
    /// </summary>
    public int CatalogStartupDelaySeconds { get; set; } = 60;

    /// <summary>
    /// Пауза между пачками enrichment при наличии работы (сек, минимум).
    /// </summary>
    public int EnrichmentBatchDelaySecondsMin { get; set; } = 15;

    /// <summary>
    /// Пауза между пачками enrichment при наличии работы (сек, максимум).
    /// </summary>
    public int EnrichmentBatchDelaySecondsMax { get; set; } = 30;

    /// <summary>
    /// Сколько раз ждать появления связки в каталоге, прежде чем закрыть торги.
    /// </summary>
    public int MaxNoLinkWaitAttempts { get; set; } = 2;

    /// <summary>
    /// Интервал между попытками обогащения, когда не хватает связки в каталоге (часы).
    /// </summary>
    public int NoLinkRecheckIntervalHours { get; set; } = 12;

    public int GetActionDelayMs()
    {
        var baseMs = Math.Max(500, RequestDelayMs);
        var jitter = Math.Max(0, RequestDelayJitterMs);
        return baseMs + (jitter > 0 ? Random.Shared.Next(0, jitter + 1) : 0);
    }

    public int GetImageDelayMs()
    {
        var baseMs = Math.Max(200, ImageDownloadDelayMs);
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
