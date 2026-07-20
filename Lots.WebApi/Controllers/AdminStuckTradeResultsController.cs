using System.Text.RegularExpressions;
using FedresursScraper.Services;
using FedresursScraper.Services.Enrichments;
using FedresursScraper.Services.Utils;
using Lots.Data.Dto;
using Lots.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FedresursScraper.Controllers;

/// <summary>
/// Админский разбор торгов, у которых много безуспешных попыток получить результаты с Федресурса.
/// </summary>
[ApiController]
[Route("api/admin/stuck-trade-results")]
[Authorize]
public class AdminStuckTradeResultsController : ControllerBase
{
    private readonly LotsDbContext _dbContext;
    private readonly IIndexNowService _indexNowService;
    private readonly ICdtTradeStatusScraper _cdtTradeStatusScraper;

    private static readonly HashSet<string> AllowedResultKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "not_held",      // Торги не состоялись
        "completed",     // Завершенные (результаты торгов)
        "cancelled",     // Торги отменены
        "no_data"        // Торги завершены (нет данных)
    };

    public AdminStuckTradeResultsController(
        LotsDbContext dbContext,
        IIndexNowService indexNowService,
        ICdtTradeStatusScraper cdtTradeStatusScraper)
    {
        _dbContext = dbContext;
        _indexNowService = indexNowService;
        _cdtTradeStatusScraper = cdtTradeStatusScraper;
    }

    private async Task<bool> IsAdminAsync()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var userId)) return false;
        var user = await _dbContext.Users.FindAsync(userId);
        return user?.IsAdmin == true;
    }

    /// <summary>
    /// Торги с высоким StatusCheckAttempts, ещё не финализированные.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetStuckBiddings(
        [FromQuery] int minAttempts = 5,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        [FromQuery] string? platform = null)
    {
        if (!await IsAdminAsync()) return Forbid();

        minAttempts = Math.Max(1, minAttempts);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var baseQuery = _dbContext.Biddings
            .AsNoTracking()
            .Where(b => !b.IsTradeStatusesFinalized && b.StatusCheckAttempts >= minAttempts);

        var platformOptions = await baseQuery
            .GroupBy(b => b.Platform)
            .Select(g => new { Platform = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Platform)
            .ToListAsync();

        var query = baseQuery;
        if (!string.IsNullOrWhiteSpace(platform))
        {
            query = query.Where(b => b.Platform == platform);
        }

        var totalCount = await query.CountAsync();

        var finalStatuses = Lot.FinalTradeStatuses;

        var rawItems = await query
            .OrderByDescending(b => b.StatusCheckAttempts)
            .ThenBy(b => b.NextStatusCheckAt ?? DateTime.MinValue)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new
            {
                b.Id,
                b.TradeNumber,
                b.Platform,
                b.Type,
                b.StatusCheckAttempts,
                b.LastStatusCheckAt,
                b.NextStatusCheckAt,
                b.ResultsAnnouncementDate,
                ActiveLotsCount = b.Lots.Count(l =>
                    l.TradeStatus == null
                    || l.TradeStatus == ""
                    || !finalStatuses.Contains(l.TradeStatus)),
                TotalLotsCount = b.Lots.Count
            })
            .ToListAsync();

        var items = rawItems.Select(b => new
        {
            b.Id,
            b.TradeNumber,
            Platform = PlatformDisplayName.GetDisplayName(b.Platform),
            PlatformRaw = b.Platform,
            b.Type,
            b.StatusCheckAttempts,
            b.LastStatusCheckAt,
            b.NextStatusCheckAt,
            b.ResultsAnnouncementDate,
            b.ActiveLotsCount,
            b.TotalLotsCount,
            FedresursUrl = $"https://fedresurs.ru/biddings/{b.Id}",
            FedresursMessagesUrl = $"https://fedresurs.ru/biddings/{b.Id}/messages"
        });

        return Ok(new
        {
            items,
            totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            minAttempts,
            platform,
            platforms = platformOptions.Select(p => new
            {
                value = p.Platform,
                label = PlatformDisplayName.GetDisplayName(p.Platform),
                count = p.Count
            })
        });
    }

    [HttpGet("count")]
    public async Task<IActionResult> GetCount([FromQuery] int minAttempts = 5)
    {
        if (!await IsAdminAsync()) return Forbid();

        minAttempts = Math.Max(1, minAttempts);

        var count = await _dbContext.Biddings
            .CountAsync(b => !b.IsTradeStatusesFinalized && b.StatusCheckAttempts >= minAttempts);

        return Ok(new { count, minAttempts });
    }

    public class PreviewPlatformStatusRequest
    {
        public List<Guid> BiddingIds { get; set; } = [];
    }

    public class PlatformStatusLotDto
    {
        public Guid LotId { get; set; }
        public string? LotNumber { get; set; }
        public int PublicId { get; set; }
        public string? PlatformStatus { get; set; }
        public bool IsFinal { get; set; }
    }

    public class PlatformStatusPreviewDto
    {
        public Guid BiddingId { get; set; }
        public string? TradeNumber { get; set; }
        public string? PlatformKind { get; set; }
        public string? PlatformLabel { get; set; }
        public string? PlatformStatus { get; set; }
        public bool IsFinal { get; set; }
        public string? SuggestedResultKind { get; set; }
        public string? Source { get; set; }
        public string? Error { get; set; }
        public List<PlatformStatusLotDto> Lots { get; set; } = [];
    }

    /// <summary>
    /// Массово подтягивает статусы с площадки для выбранных торгов (без записи в БД).
    /// ЦДТ — live-парсинг страницы торгов; Альфалот/РАД — статус из каталожных связей.
    /// </summary>
    [HttpPost("preview-platform-status")]
    public async Task<IActionResult> PreviewPlatformStatus(
        [FromBody] PreviewPlatformStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync()) return Forbid();

        var biddingIds = (request?.BiddingIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(50)
            .ToList();

        if (biddingIds.Count == 0)
            return BadRequest(new { message = "Выберите хотя бы одни торги." });

        var biddings = await _dbContext.Biddings
            .AsNoTracking()
            .Include(b => b.Lots)
            .Where(b => biddingIds.Contains(b.Id))
            .ToListAsync(cancellationToken);

        var byId = biddings.ToDictionary(b => b.Id);
        using var gate = new SemaphoreSlim(3);

        var tasks = biddingIds.Select(async biddingId =>
        {
            if (!byId.TryGetValue(biddingId, out var bidding))
            {
                return new PlatformStatusPreviewDto
                {
                    BiddingId = biddingId,
                    Error = "Торги не найдены."
                };
            }

            var kind = DetectPlatformKind(bidding.Platform);
            try
            {
                return kind switch
                {
                    "cdt" => await PreviewCdtStatusAsync(bidding, gate, cancellationToken),
                    "alfalot" => await PreviewAlfalotStatusAsync(bidding, cancellationToken),
                    "rad" => await PreviewRadStatusAsync(bidding, cancellationToken),
                    "mets" => UnsupportedPreview(bidding, "mets", "МЭТС",
                        "Для МЭТС live-проверка статуса пока не реализована."),
                    _ => UnsupportedPreview(bidding, kind, PlatformDisplayName.GetDisplayName(bidding.Platform),
                        "Площадка не поддерживается для проверки статуса.")
                };
            }
            catch (Exception ex)
            {
                return new PlatformStatusPreviewDto
                {
                    BiddingId = bidding.Id,
                    TradeNumber = bidding.TradeNumber,
                    PlatformKind = kind,
                    PlatformLabel = PlatformDisplayName.GetDisplayName(bidding.Platform),
                    Error = ex.Message
                };
            }
        });

        var results = await Task.WhenAll(tasks);
        var byResultId = results.ToDictionary(r => r.BiddingId);
        var items = biddingIds
            .Where(id => byResultId.ContainsKey(id))
            .Select(id => byResultId[id])
            .ToList();

        return Ok(new { items });
    }

    private static PlatformStatusPreviewDto UnsupportedPreview(
        Bidding bidding,
        string? kind,
        string? label,
        string error) =>
        new()
        {
            BiddingId = bidding.Id,
            TradeNumber = bidding.TradeNumber,
            PlatformKind = kind,
            PlatformLabel = label,
            Error = error
        };

    private async Task<PlatformStatusPreviewDto> PreviewCdtStatusAsync(
        Bidding bidding,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var status = await _cdtTradeStatusScraper.GetTradeStatusAsync(
                bidding.TradeNumber,
                cancellationToken);

            var isFinal = IsFinalForAdminPreview(status);
            return new PlatformStatusPreviewDto
            {
                BiddingId = bidding.Id,
                TradeNumber = bidding.TradeNumber,
                PlatformKind = "cdt",
                PlatformLabel = "ЦДТ",
                PlatformStatus = status,
                IsFinal = isFinal,
                SuggestedResultKind = SuggestResultKind(status),
                Source = "live",
                Error = status == null ? "Не удалось получить статус со страницы ЦДТ." : null,
                Lots = bidding.Lots
                    .OrderBy(l => NormalizeLotNumber(l.LotNumber))
                    .Select(l => new PlatformStatusLotDto
                    {
                        LotId = l.Id,
                        LotNumber = l.LotNumber,
                        PublicId = l.PublicId,
                        PlatformStatus = status,
                        IsFinal = isFinal
                    })
                    .ToList()
            };
        }
        finally
        {
            gate.Release();
            await Task.Delay(200, cancellationToken);
        }
    }

    private async Task<PlatformStatusPreviewDto> PreviewAlfalotStatusAsync(
        Bidding bidding,
        CancellationToken cancellationToken)
    {
        var tradeNorm = AlfalotHtmlParser.NormalizeTradeNumber(bidding.TradeNumber);
        var links = await _dbContext.AlfalotLotLinks
            .AsNoTracking()
            .Where(x => x.TradeNumberNormalized == tradeNorm)
            .Select(x => new { x.LotNumberNormalized, x.Status })
            .ToListAsync(cancellationToken);

        return BuildCatalogPreview(
            bidding,
            "alfalot",
            "Альфалот",
            links.Select(x => (x.LotNumberNormalized, x.Status)).ToList(),
            "В индексе Альфалот нет статуса для этих лотов.");
    }

    private async Task<PlatformStatusPreviewDto> PreviewRadStatusAsync(
        Bidding bidding,
        CancellationToken cancellationToken)
    {
        var tradeNorm = AlfalotHtmlParser.NormalizeTradeNumber(bidding.TradeNumber);
        var links = await _dbContext.RadLotLinks
            .AsNoTracking()
            .Where(x => x.EfrsbLotIdNormalized == tradeNorm)
            .Select(x => new { x.LotNumberNormalized, x.Status })
            .ToListAsync(cancellationToken);

        return BuildCatalogPreview(
            bidding,
            "rad",
            "РАД",
            links.Select(x => (x.LotNumberNormalized, x.Status)).ToList(),
            "В индексе РАД нет статуса для этих лотов.");
    }

    private static PlatformStatusPreviewDto BuildCatalogPreview(
        Bidding bidding,
        string kind,
        string label,
        List<(string LotNumberNormalized, string? Status)> links,
        string missingError)
    {
        var lotStatuses = bidding.Lots
            .OrderBy(l => NormalizeLotNumber(l.LotNumber))
            .Select(l =>
            {
                var lotNorm = AlfalotHtmlParser.NormalizeLotNumber(l.LotNumber);
                var status = links.FirstOrDefault(x => x.LotNumberNormalized == lotNorm).Status;
                return new PlatformStatusLotDto
                {
                    LotId = l.Id,
                    LotNumber = l.LotNumber,
                    PublicId = l.PublicId,
                    PlatformStatus = status,
                    IsFinal = IsFinalForAdminPreview(status)
                };
            })
            .ToList();

        var distinct = lotStatuses
            .Select(x => x.PlatformStatus)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        var platformStatus = distinct.Count switch
        {
            0 => null,
            1 => distinct[0],
            _ => string.Join(" / ", distinct)
        };

        var anyStatus = distinct.Count > 0;
        var allFinal = lotStatuses.Count > 0 && lotStatuses.All(x => x.IsFinal);

        return new PlatformStatusPreviewDto
        {
            BiddingId = bidding.Id,
            TradeNumber = bidding.TradeNumber,
            PlatformKind = kind,
            PlatformLabel = label,
            PlatformStatus = platformStatus,
            IsFinal = allFinal && anyStatus,
            SuggestedResultKind = distinct.Count == 1 ? SuggestResultKind(distinct[0]) : null,
            Source = "catalog",
            Error = !anyStatus ? missingError : null,
            Lots = lotStatuses
        };
    }

    private static string? DetectPlatformKind(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return null;
        if (platform.Contains("Межрегиональная Электронная Торговая Система", StringComparison.OrdinalIgnoreCase)
            || platform.Contains("МЭТС", StringComparison.OrdinalIgnoreCase))
            return "mets";
        if (platform.Contains("Центр дистанционных торгов", StringComparison.OrdinalIgnoreCase))
            return "cdt";
        if (platform.Contains("Альфалот", StringComparison.OrdinalIgnoreCase))
            return "alfalot";
        if (platform.Contains("Российский аукционный дом", StringComparison.OrdinalIgnoreCase))
            return "rad";
        return null;
    }

    private static bool IsFinalForAdminPreview(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;

        if (Lot.FinalTradeStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            return true;

        if (status.Contains("приостанов", StringComparison.OrdinalIgnoreCase))
            return false;

        if (status.Contains("не состоял", StringComparison.OrdinalIgnoreCase))
            return true;
        if (status.Contains("отменен", StringComparison.OrdinalIgnoreCase)
            || status.Contains("отменён", StringComparison.OrdinalIgnoreCase))
            return true;
        if (status.Contains("завершен", StringComparison.OrdinalIgnoreCase)
            || status.Contains("окончен", StringComparison.OrdinalIgnoreCase))
            return true;
        if (status.Contains("аннулир", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string? SuggestResultKind(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;

        if (status.Contains("не состоял", StringComparison.OrdinalIgnoreCase))
            return "not_held";
        if (status.Contains("отменен", StringComparison.OrdinalIgnoreCase)
            || status.Contains("отменён", StringComparison.OrdinalIgnoreCase))
            return "cancelled";
        if (status.Contains("завершен", StringComparison.OrdinalIgnoreCase)
            || status.Contains("окончен", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Завершенные", StringComparison.OrdinalIgnoreCase))
            return "completed";

        return null;
    }

    /// <summary>
    /// Детали торгов и активных лотов для ручной фиксации результатов.
    /// </summary>
    [HttpGet("{biddingId:guid}")]
    public async Task<IActionResult> GetBiddingDetails(Guid biddingId)
    {
        if (!await IsAdminAsync()) return Forbid();

        var bidding = await _dbContext.Biddings
            .AsNoTracking()
            .Include(b => b.Lots)
            .FirstOrDefaultAsync(b => b.Id == biddingId);

        if (bidding == null)
            return NotFound(new { message = "Торги не найдены." });

        var platformLinks = await ResolvePlatformLinksAsync(bidding);

        var lots = bidding.Lots
            .OrderBy(l => NormalizeLotNumber(l.LotNumber))
            .ThenBy(l => l.PublicId)
            .Select(l =>
            {
                var slug = !string.IsNullOrWhiteSpace(l.Slug)
                    ? l.Slug
                    : (!string.IsNullOrWhiteSpace(l.Title) ? SlugHelper.GenerateSlug(l.Title) : "lot");

                var lotNumberNorm = AlfalotHtmlParser.NormalizeLotNumber(l.LotNumber);
                platformLinks.LotUrls.TryGetValue(lotNumberNorm, out var platformLotUrl);

                return new
                {
                    l.Id,
                    l.PublicId,
                    l.LotNumber,
                    l.Title,
                    l.StartPrice,
                    l.TradeStatus,
                    l.FinalPrice,
                    l.WinnerName,
                    l.WinnerInn,
                    IsActive = l.IsActive(),
                    Url = $"/lot/{slug}-{l.PublicId}",
                    PlatformLotUrl = platformLotUrl
                };
            })
            .ToList();

        return Ok(new
        {
            bidding.Id,
            bidding.TradeNumber,
            Platform = PlatformDisplayName.GetDisplayName(bidding.Platform),
            PlatformRaw = bidding.Platform,
            PlatformKind = platformLinks.Kind,
            PlatformUrl = platformLinks.TradeUrl,
            PlatformLabel = platformLinks.Label,
            bidding.Type,
            bidding.StatusCheckAttempts,
            bidding.LastStatusCheckAt,
            bidding.NextStatusCheckAt,
            bidding.ResultsAnnouncementDate,
            bidding.IsTradeStatusesFinalized,
            bidding.BidAcceptancePeriod,
            bidding.TradePeriod,
            FedresursUrl = $"https://fedresurs.ru/biddings/{bidding.Id}",
            FedresursMessagesUrl = $"https://fedresurs.ru/biddings/{bidding.Id}/messages",
            lots
        });
    }

    public class ManualLotResultDto
    {
        public Guid LotId { get; set; }

        /// <summary>
        /// not_held | completed | cancelled | no_data
        /// </summary>
        public string ResultKind { get; set; } = default!;

        public string? Reason { get; set; }
        public decimal? FinalPrice { get; set; }
        public string? WinnerName { get; set; }
        public string? WinnerInn { get; set; }
        public string? DecisionJustification { get; set; }
    }

    public class ApplyManualResultsRequest
    {
        public List<ManualLotResultDto> Lots { get; set; } = [];

        /// <summary>
        /// Если true — оставшиеся активные лоты закрыть как «нет данных» и финализировать торги.
        /// </summary>
        public bool FinalizeRemainingAsNoData { get; set; }
    }

    /// <summary>
    /// Ручная фиксация результатов торгов по лотам (источник — площадка / ручной разбор).
    /// Останавливает повторные проверки Федресурса, когда все лоты получают конечный статус.
    /// </summary>
    [HttpPost("{biddingId:guid}/apply")]
    public async Task<IActionResult> ApplyManualResults(
        Guid biddingId,
        [FromBody] ApplyManualResultsRequest request)
    {
        if (!await IsAdminAsync()) return Forbid();

        if (request == null)
            return BadRequest(new { message = "Пустой запрос." });

        request.Lots ??= [];

        if (request.Lots.Count == 0 && !request.FinalizeRemainingAsNoData)
            return BadRequest(new { message = "Укажите хотя бы один лот или включите закрытие остальных." });

        foreach (var item in request.Lots)
        {
            if (!AllowedResultKinds.Contains(item.ResultKind))
            {
                return BadRequest(new
                {
                    message = $"Неизвестный ResultKind '{item.ResultKind}'. Допустимо: not_held, completed, cancelled, no_data."
                });
            }
        }

        var bidding = await _dbContext.Biddings
            .Include(b => b.Lots)
            .FirstOrDefaultAsync(b => b.Id == biddingId);

        if (bidding == null)
            return NotFound(new { message = "Торги не найдены." });

        var urlsToPing = new List<string>();
        var auditEvents = new List<LotAuditEvent>();
        var updatedLotIds = new List<Guid>();
        var now = DateTime.UtcNow;

        foreach (var item in request.Lots)
        {
            var lot = bidding.Lots.FirstOrDefault(l => l.Id == item.LotId);
            if (lot == null)
                return BadRequest(new { message = $"Лот {item.LotId} не принадлежит торгам {biddingId}." });

            if (!lot.IsActive())
                continue;

            if (string.Equals(item.ResultKind, "no_data", StringComparison.OrdinalIgnoreCase))
            {
                if (lot.TryMarkAsFinalizedWithoutData("ManualAdminAction", out var timeoutAudit) && timeoutAudit != null)
                {
                    auditEvents.Add(timeoutAudit);
                    urlsToPing.Add(lot.GetOrGenerateLotUrl());
                    updatedLotIds.Add(lot.Id);
                }

                continue;
            }

            var (eventType, status) = MapResultKind(item.ResultKind);
            var lotNumber = NormalizeLotNumber(lot.LotNumber);
            if (string.IsNullOrWhiteSpace(lotNumber))
                lotNumber = lot.PublicId.ToString();

            var messageId = Guid.NewGuid();
            var dto = new ImportLotTradeResultDto
            {
                BiddingId = bidding.Id,
                MessageId = messageId,
                LotNumber = lotNumber,
                EventType = eventType,
                EventDate = now,
                Reason = item.Reason,
                FinalPrice = item.FinalPrice,
                WinnerName = item.WinnerName,
                WinnerInn = item.WinnerInn,
                Status = status,
                DecisionJustification = item.DecisionJustification
                    ?? "Ручная фиксация администратором (результаты с площадки / без сообщения на Федресурсе)."
            };

            _dbContext.LotTradeResults.Add(new LotTradeResult
            {
                Id = Guid.NewGuid(),
                BiddingId = bidding.Id,
                MessageId = messageId,
                LotNumber = lotNumber,
                EventType = eventType,
                EventDate = now,
                Reason = item.Reason,
                FinalPrice = item.FinalPrice,
                WinnerName = item.WinnerName,
                WinnerInn = item.WinnerInn,
                Status = status,
                DecisionJustification = dto.DecisionJustification,
                CreatedAt = now,
                IsExportedToProd = true
            });

            lot.UpdateTradeStatus(dto, "ManualAdminAction", out var auditEvent);
            auditEvents.Add(auditEvent);
            urlsToPing.Add(lot.GetOrGenerateLotUrl());
            updatedLotIds.Add(lot.Id);
        }

        if (request.FinalizeRemainingAsNoData)
        {
            var remaining = bidding.ForceFinalizeMissingResults("ManualAdminAction", out var finalizeAudits);
            auditEvents.AddRange(finalizeAudits);
            foreach (var lot in remaining)
            {
                urlsToPing.Add(lot.GetOrGenerateLotUrl());
                updatedLotIds.Add(lot.Id);
            }
        }
        else if (bidding.Lots.Where(l => !string.IsNullOrWhiteSpace(l.LotNumber)).All(l => !l.IsActive())
                 || bidding.Lots.All(l => !l.IsActive()))
        {
            bidding.IsTradeStatusesFinalized = true;
            bidding.NextStatusCheckAt = null;
        }

        if (auditEvents.Count > 0)
            _dbContext.LotAuditEvents.AddRange(auditEvents);

        await _dbContext.SaveChangesAsync();

        if (urlsToPing.Count > 0)
        {
            var distinctUrls = urlsToPing.Distinct().ToList();
            _ = _indexNowService.SubmitUrlsAsync(distinctUrls);
        }

        return Ok(new
        {
            message = "Результаты зафиксированы.",
            biddingId,
            updatedLots = updatedLotIds.Distinct().Count(),
            isTradeStatusesFinalized = bidding.IsTradeStatusesFinalized,
            nextStatusCheckAt = bidding.NextStatusCheckAt
        });
    }

    /// <summary>
    /// Быстрое закрытие всех активных лотов как «нет данных» и остановка проверок.
    /// </summary>
    [HttpPost("{biddingId:guid}/force-finalize")]
    public async Task<IActionResult> ForceFinalize(Guid biddingId)
    {
        if (!await IsAdminAsync()) return Forbid();

        var bidding = await _dbContext.Biddings
            .Include(b => b.Lots)
            .FirstOrDefaultAsync(b => b.Id == biddingId);

        if (bidding == null)
            return NotFound(new { message = "Торги не найдены." });

        var changedLots = bidding.ForceFinalizeMissingResults("ManualAdminAction", out var auditEvents);

        if (auditEvents.Count > 0)
            _dbContext.LotAuditEvents.AddRange(auditEvents);

        await _dbContext.SaveChangesAsync();

        if (changedLots.Count > 0)
        {
            var urls = changedLots.Select(l => l.GetOrGenerateLotUrl()).Distinct().ToList();
            _ = _indexNowService.SubmitUrlsAsync(urls);
        }

        return Ok(new
        {
            message = "Торги закрыты без данных.",
            biddingId,
            processedLots = changedLots.Count
        });
    }

    private static (string EventType, string Status) MapResultKind(string resultKind) =>
        resultKind.ToLowerInvariant() switch
        {
            "not_held" => ("Торги не состоялись", "Торги не состоялись"),
            "cancelled" => ("Отмена торгов", "Торги отменены"),
            "completed" => ("Результаты торгов", "Завершенные"),
            _ => throw new ArgumentOutOfRangeException(nameof(resultKind), resultKind, null)
        };

    private sealed class PlatformLinks
    {
        public string? Kind { get; init; }
        public string? Label { get; init; }
        public string? TradeUrl { get; init; }
        public Dictionary<string, string> LotUrls { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<PlatformLinks> ResolvePlatformLinksAsync(Bidding bidding)
    {
        var platform = bidding.Platform ?? string.Empty;
        var tradeNorm = AlfalotHtmlParser.NormalizeTradeNumber(bidding.TradeNumber);

        if (platform.Contains("Межрегиональная Электронная Торговая Система", StringComparison.OrdinalIgnoreCase)
            || platform.Contains("МЭТС", StringComparison.OrdinalIgnoreCase))
        {
            return new PlatformLinks
            {
                Kind = "mets",
                Label = "МЭТС",
                TradeUrl = BuildMetsTradeUrl(bidding.TradeNumber)
            };
        }

        if (platform.Contains("Центр дистанционных торгов", StringComparison.OrdinalIgnoreCase))
        {
            var tradeId = Regex.Replace(bidding.TradeNumber ?? string.Empty, @"\D", "");
            return new PlatformLinks
            {
                Kind = "cdt",
                Label = "ЦДТ",
                TradeUrl = string.IsNullOrWhiteSpace(tradeId) ? null : $"https://torgi.cdtrf.ru/trades/{tradeId}"
            };
        }

        if (platform.Contains("Альфалот", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(tradeNorm))
        {
            var links = await _dbContext.AlfalotLotLinks
                .AsNoTracking()
                .Where(x => x.TradeNumberNormalized == tradeNorm)
                .Select(x => new { x.LotNumberNormalized, x.TradeUrl, x.LotUrl })
                .ToListAsync();

            return new PlatformLinks
            {
                Kind = "alfalot",
                Label = "Альфалот",
                TradeUrl = links.Select(x => x.TradeUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)),
                LotUrls = links
                    .Where(x => !string.IsNullOrWhiteSpace(x.LotNumberNormalized) && !string.IsNullOrWhiteSpace(x.LotUrl))
                    .GroupBy(x => x.LotNumberNormalized)
                    .ToDictionary(g => g.Key, g => g.First().LotUrl, StringComparer.OrdinalIgnoreCase)
            };
        }

        if (platform.Contains("Российский аукционный дом", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(tradeNorm))
        {
            var links = await _dbContext.RadLotLinks
                .AsNoTracking()
                .Where(x => x.EfrsbLotIdNormalized == tradeNorm)
                .Select(x => new { x.LotNumberNormalized, x.LotUrl })
                .ToListAsync();

            return new PlatformLinks
            {
                Kind = "rad",
                Label = "РАД",
                TradeUrl = links.Select(x => x.LotUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)),
                LotUrls = links
                    .Where(x => !string.IsNullOrWhiteSpace(x.LotNumberNormalized) && !string.IsNullOrWhiteSpace(x.LotUrl))
                    .GroupBy(x => x.LotNumberNormalized)
                    .ToDictionary(g => g.Key, g => g.First().LotUrl, StringComparer.OrdinalIgnoreCase)
            };
        }

        return new PlatformLinks();
    }

    /// <summary>
    /// Как в MetsEnrichmentService: "190006-МЭТС-1" → https://m-ets.ru/190006-1
    /// </summary>
    private static string? BuildMetsTradeUrl(string? tradeNumber)
    {
        if (string.IsNullOrWhiteSpace(tradeNumber)) return null;
        var cleanNumber = tradeNumber.Replace("-МЭТС", "", StringComparison.OrdinalIgnoreCase).Trim();
        return string.IsNullOrWhiteSpace(cleanNumber) ? null : $"https://m-ets.ru/{cleanNumber}";
    }

    private static string NormalizeLotNumber(string? lotNumber)
    {
        if (string.IsNullOrWhiteSpace(lotNumber)) return string.Empty;
        return Regex.Replace(lotNumber.Trim(), @"(?i)\s*лот\s*№?\s*", "").Trim();
    }

}
