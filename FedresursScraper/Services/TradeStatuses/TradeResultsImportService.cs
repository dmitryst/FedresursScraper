using System.Text.RegularExpressions;
using Lots.Data;
using Lots.Data.Entities;
using Lots.Data.Dto;
using Microsoft.EntityFrameworkCore;

namespace FedresursScraper.Services;

/// <summary>
/// Сервис импорта результатов (для прода)
/// 
/// Сервис принимает массив фактов, сохраняет их, обновляет доменные модели лотов и торгов, 
/// генерирует события аудита и пушит новые URL-адреса в s-lot.ru для переиндексации через Яндекс.
/// </summary>
public class TradeResultsImportService
{
    private readonly LotsDbContext _dbContext;
    private readonly IIndexNowService _indexNowService;
    private readonly ILogger<TradeResultsImportService> _logger;

    public TradeResultsImportService(
        LotsDbContext dbContext,
        IIndexNowService indexNowService,
        ILogger<TradeResultsImportService> logger)
    {
        _dbContext = dbContext;
        _indexNowService = indexNowService;
        _logger = logger;
    }

    public async Task ImportResultsAsync(List<ImportLotTradeResultDto> incomingResults, CancellationToken stoppingToken)
    {
        if (!incomingResults.Any()) return;

        // Группируем входящие результаты по торгам, чтобы минимизировать запросы к БД
        var biddingsGroups = incomingResults.GroupBy(r => r.BiddingId).ToList();
        var urlsToPing = new List<string>();

        foreach (var group in biddingsGroups)
        {
            var biddingId = group.Key;

            var bidding = await _dbContext.Biddings
                .Include(b => b.Lots)
                .FirstOrDefaultAsync(b => b.Id == biddingId, stoppingToken);

            if (bidding == null)
            {
                _logger.LogWarning("Торги {BiddingId} не найдены при импорте результатов.", biddingId);
                continue;
            }

            // Получаем MessageId, которые уже сохранены на проде, чтобы избежать дублей при повторных запросах
            var existingMessageIds = await _dbContext.LotTradeResults
                .Where(r => r.BiddingId == biddingId)
                .Select(r => r.MessageId)
                .ToListAsync(stoppingToken);

            bool biddingHasChanges = false;

            foreach (var dto in group)
            {
                if (existingMessageIds.Contains(dto.MessageId)) continue;

                // 1. Сохраняем сам факт (результат парсинга)
                var tradeResult = new LotTradeResult
                {
                    Id = Guid.NewGuid(),
                    BiddingId = biddingId,
                    MessageId = dto.MessageId,
                    LotNumber = dto.LotNumber,
                    EventType = dto.EventType,
                    EventDate = dto.EventDate.Kind == DateTimeKind.Utc ? dto.EventDate : DateTime.SpecifyKind(dto.EventDate, DateTimeKind.Utc),
                    Reason = dto.Reason,
                    FinalPrice = dto.FinalPrice,
                    WinnerName = dto.WinnerName,
                    WinnerInn = dto.WinnerInn,
                    CreatedAt = DateTime.UtcNow,
                    Status = dto.Status,
                    DecisionJustification = dto.DecisionJustification,
                    IsExportedToProd = true // На проде это уже импортировано
                };

                _dbContext.LotTradeResults.Add(tradeResult);

                // 2. Обновляем статус лота
                var lot = bidding.Lots.FirstOrDefault(l => NormalizeLotNumber(l.LotNumber) == dto.LotNumber);
                if (lot != null && lot.IsActive())
                {
                    if (lot.UpdateTradeStatus(dto, "ParserImport", out var auditEvent))
                    {
                        _dbContext.LotAuditEvents.Add(auditEvent);
                        urlsToPing.Add(GenerateLotUrl(lot));
                        biddingHasChanges = true;
                    }
                }
            }

            // 3. Проверяем, нужно ли финализировать торги
            if (biddingHasChanges)
            {
                if (bidding.Lots.Where(l => !string.IsNullOrWhiteSpace(l.LotNumber)).All(l => !l.IsActive()))
                {
                    bidding.IsTradeStatusesFinalized = true;
                    bidding.NextStatusCheckAt = null;
                    _logger.LogInformation("Все лоты для торгов {BiddingId} завершены. Торги закрыты.", biddingId);
                }
                else
                {
                    // Если торги не завершились полностью, вычисляем дату следующего чека
                    bidding.ScheduleNextCheck(DateTime.UtcNow);
                }
            }
        }

        // Сохраняем изменения в базу
        await _dbContext.SaveChangesAsync(stoppingToken);

        // Пингуем Яндекс для обновления индекса
        if (urlsToPing.Any())
        {
            var distinctUrls = urlsToPing.Distinct().ToList();
            await _indexNowService.SubmitUrlsAsync(distinctUrls);
            _logger.LogInformation("IndexNow: Отправлено {Count} ссылок завершенных лотов.", distinctUrls.Count);
        }
    }

    private string NormalizeLotNumber(string? lotNumber)
    {
        if (string.IsNullOrWhiteSpace(lotNumber)) return string.Empty;
        return Regex.Replace(lotNumber.Trim(), @"(?i)\s*лот\s*№?\s*", "").Trim();
    }

    private string GenerateLotUrl(Lot lot)
    {
        var slug = lot.Slug;

        if (string.IsNullOrWhiteSpace(slug))
        {
            var textForSlug = !string.IsNullOrWhiteSpace(lot.Title)
                ? lot.Title
                : lot.Description;

            slug = SlugHelper.GenerateSlug(textForSlug ?? "lot");
        }

        return $"https://s-lot.ru/lot/{slug}-{lot.PublicId}";
    }
}