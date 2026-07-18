using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FedresursScraper.Services.Enrichments;

namespace FedresursScraper.Services;

public interface IRadCatalogIndexerService
{
    Task<int> IndexCatalogAsync(CancellationToken ct);
}

/// <summary>
/// Индексация каталога РАД (имущество должников) через HttpClient + AngleSharp/HAP.
/// Для новых product_id дополнительно читаем e-auction, чтобы получить
/// «Идентификатор лота в ЕФРСБ».
/// </summary>
public class RadCatalogIndexerService : IRadCatalogIndexerService
{
    private static DateTime? _lastFullRescanUtc;

    private readonly LotsDbContext _context;
    private readonly HttpClient _httpClient;
    private readonly ILogger<RadCatalogIndexerService> _logger;
    private readonly IOptionsMonitor<RadEnrichmentOptions> _options;

    public RadCatalogIndexerService(
        LotsDbContext context,
        HttpClient httpClient,
        ILogger<RadCatalogIndexerService> logger,
        IOptionsMonitor<RadEnrichmentOptions> options)
    {
        _context = context;
        _httpClient = httpClient;
        _logger = logger;
        _options = options;
    }

    public async Task<int> IndexCatalogAsync(CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var upserted = 0;
        var inserted = 0;
        var consecutivePagesWithoutNew = 0;
        var stopAfterPagesWithoutNew = Math.Max(1, options.CatalogStopAfterPagesWithoutNew);
        var fullRescanHours = Math.Max(1, options.CatalogFullRescanIntervalHours);

        if (!_lastFullRescanUtc.HasValue)
            _lastFullRescanUtc = DateTime.UtcNow;

        var forceFullRescan =
            DateTime.UtcNow - _lastFullRescanUtc.Value >= TimeSpan.FromHours(fullRescanHours);

        _logger.LogInformation(
            "Старт индексации каталога РАД. Mode={Mode}, MaxPages={MaxPages}, StopAfterNoNew={StopAfter}",
            forceFullRescan ? "full" : "incremental",
            options.CatalogMaxPages,
            stopAfterPagesWithoutNew);

        for (var page = 1; page <= options.CatalogMaxPages; page++)
        {
            ct.ThrowIfCancellationRequested();

            var catalogUrl = RadHtmlParser.BuildCatalogPageUrl(page);
            _logger.LogInformation("РАД каталог: загрузка стр.{Page}: {Url}", page, catalogUrl);

            string catalogHtml;
            try
            {
                catalogHtml = await _httpClient.GetStringAsync(catalogUrl, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось загрузить стр.{Page} каталога РАД", page);
                break;
            }

            var items = RadHtmlParser.ParseCatalogItems(catalogHtml);
            if (items.Count == 0)
            {
                _logger.LogWarning("На странице {Page} каталога РАД не найдено лотов.", page);
                break;
            }

            var pageInserted = 0;
            var pageUpserts = 0;
            var pageSkippedFinished = 0;
            var pageSkippedNoEfrsb = 0;

            foreach (var item in items)
            {
                if (RadHtmlParser.IsFinishedStatus(item.Status))
                {
                    pageSkippedFinished++;
                    continue;
                }

                var isNew = await UpsertItemAsync(item, options, ct);
                if (isNew == null)
                {
                    pageSkippedNoEfrsb++;
                    continue;
                }

                pageUpserts++;
                upserted++;
                if (isNew.Value)
                {
                    pageInserted++;
                    inserted++;
                }
            }

            _logger.LogInformation(
                "РАД каталог стр.{Page}: новых {New}, обновлено {Updated}, завершённых {Finished}, без ЕФРСБ {NoEfrsb}, всего {Total}",
                page,
                pageInserted,
                pageUpserts - pageInserted,
                pageSkippedFinished,
                pageSkippedNoEfrsb,
                items.Count);

            if (!forceFullRescan && pageUpserts > 0 && pageInserted == 0)
            {
                consecutivePagesWithoutNew++;
                if (consecutivePagesWithoutNew >= stopAfterPagesWithoutNew)
                {
                    _logger.LogInformation(
                        "Страница {Page} без новых записей — ранний выход (инкрементальный обход).",
                        page);
                    await _context.SaveChangesAsync(ct);
                    break;
                }
            }
            else
            {
                consecutivePagesWithoutNew = 0;
            }

            await _context.SaveChangesAsync(ct);

            if (page < options.CatalogMaxPages)
            {
                var delayMs = options.GetActionDelayMs();
                _logger.LogDebug("Пауза перед следующей страницей каталога РАД: {DelayMs} мс", delayMs);
                await Task.Delay(delayMs, ct);
            }
        }

        if (forceFullRescan)
            _lastFullRescanUtc = DateTime.UtcNow;

        _logger.LogInformation(
            "Индексация каталога РАД завершена. Upsert={Count}, New={New}, Mode={Mode}",
            upserted,
            inserted,
            forceFullRescan ? "full" : "incremental");

        return upserted;
    }

    /// <returns>
    /// true — новая запись; false — обновление; null — не удалось получить идентификатор ЕФРСБ.
    /// </returns>
    private async Task<bool?> UpsertItemAsync(
        RadHtmlParser.CatalogItem item,
        RadEnrichmentOptions options,
        CancellationToken ct)
    {
        var existingByProduct = await _context.RadLotLinks
            .FirstOrDefaultAsync(x => x.ProductId == item.ProductId, ct);

        // Уже есть связка с ЕФРСБ — обновляем метаданные без повторного запроса e-auction.
        if (existingByProduct != null && !string.IsNullOrWhiteSpace(existingByProduct.EfrsbLotId))
        {
            if (!string.IsNullOrWhiteSpace(item.LotNumber))
            {
                existingByProduct.LotNumber = item.LotNumber;
                existingByProduct.LotNumberNormalized = RadHtmlParser.NormalizeLotNumber(item.LotNumber);
            }

            if (!string.IsNullOrWhiteSpace(item.LotCode))
                existingByProduct.LotCode = item.LotCode;

            if (!string.IsNullOrWhiteSpace(item.Status))
                existingByProduct.Status = item.Status;

            existingByProduct.LotUrl = item.LotUrl;
            existingByProduct.UpdatedAt = DateTime.UtcNow;
            return false;
        }

        await Task.Delay(options.GetActionDelayMs(), ct);

        string productHtml;
        try
        {
            productHtml = await _httpClient.GetStringAsync(item.LotUrl, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "РАД: не удалось открыть лот product_id={ProductId}", item.ProductId);
            return null;
        }

        var lotUnid = RadHtmlParser.ExtractLotUnid(productHtml);
        if (string.IsNullOrWhiteSpace(lotUnid))
        {
            _logger.LogWarning("РАД: не найден lotUnid для product_id={ProductId}", item.ProductId);
            return null;
        }

        await Task.Delay(options.GetActionDelayMs(), ct);

        string eAuctionHtml;
        try
        {
            eAuctionHtml = await _httpClient.GetStringAsync(RadHtmlParser.BuildEAuctionUrl(lotUnid), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "РАД: не удалось загрузить e-auction для product_id={ProductId}, lotUnid={LotUnid}",
                item.ProductId,
                lotUnid);
            return null;
        }

        var efrsbLotId = RadHtmlParser.ExtractEfrsbLotId(eAuctionHtml);
        if (string.IsNullOrWhiteSpace(efrsbLotId))
        {
            _logger.LogWarning(
                "РАД: не найден «Идентификатор лота в ЕФРСБ» для product_id={ProductId}",
                item.ProductId);
            return null;
        }

        var lotNumber = item.LotNumber
                        ?? RadHtmlParser.ExtractLotNumber(item.Title, productHtml)
                        ?? "1";
        var lotCode = item.LotCode ?? RadHtmlParser.ExtractLotCode(productHtml);
        var efrsbNorm = RadHtmlParser.NormalizeEfrsbLotId(efrsbLotId);
        var lotNorm = RadHtmlParser.NormalizeLotNumber(lotNumber);

        var existingByKey = await _context.RadLotLinks
            .FirstOrDefaultAsync(
                x => x.EfrsbLotIdNormalized == efrsbNorm && x.LotNumberNormalized == lotNorm,
                ct);

        if (existingByKey != null)
        {
            existingByKey.EfrsbLotId = efrsbLotId;
            existingByKey.LotNumber = lotNumber;
            existingByKey.ProductId = item.ProductId;
            existingByKey.LotUrl = item.LotUrl;
            existingByKey.LotCode = lotCode ?? existingByKey.LotCode;
            existingByKey.Status = item.Status ?? existingByKey.Status;
            existingByKey.UpdatedAt = DateTime.UtcNow;
            return false;
        }

        if (existingByProduct != null)
        {
            existingByProduct.EfrsbLotId = efrsbLotId;
            existingByProduct.EfrsbLotIdNormalized = efrsbNorm;
            existingByProduct.LotNumber = lotNumber;
            existingByProduct.LotNumberNormalized = lotNorm;
            existingByProduct.LotUrl = item.LotUrl;
            existingByProduct.LotCode = lotCode;
            existingByProduct.Status = item.Status;
            existingByProduct.UpdatedAt = DateTime.UtcNow;
            return false;
        }

        _context.RadLotLinks.Add(new RadLotLink
        {
            Id = Guid.NewGuid(),
            EfrsbLotId = efrsbLotId,
            EfrsbLotIdNormalized = efrsbNorm,
            LotNumber = lotNumber,
            LotNumberNormalized = lotNorm,
            ProductId = item.ProductId,
            LotCode = lotCode,
            LotUrl = item.LotUrl,
            Status = item.Status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        return true;
    }
}
