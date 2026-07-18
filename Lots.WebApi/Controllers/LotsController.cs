using Microsoft.AspNetCore.Mvc;
using FedresursScraper.Services;
using Lots.Data.Specifications;
using Microsoft.EntityFrameworkCore;
using FedresursScraper.Controllers.Models;
using FedresursScraper.Controllers.Utils;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Lots.Data.Entities;
using Lots.Data;
using Ardalis.Specification;
using Lots.Data.Models;
using Lots.Application.Services.VehicleFilters;
using FedresursScraper.Services.Utils;
using Lots.Application.Interfaces;


namespace FedresursScraper.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LotsController : ControllerBase
{
    private readonly ILotCopyService _lotCopyService;
    private readonly LotsDbContext _dbContext;
    private readonly IDbContextFactory<LotsDbContext> _dbContextFactory;
    private readonly IVehicleFilterOptionsCache _vehicleFilterOptionsCache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly bool _aiQuickEvaluationAdminOnly;

    public LotsController(
        ILotCopyService lotCopyService,
        LotsDbContext dbContext,
        IDbContextFactory<LotsDbContext> dbContextFactory,
        IVehicleFilterOptionsCache vehicleFilterOptionsCache,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _lotCopyService = lotCopyService;
        _dbContext = dbContext;
        _dbContextFactory = dbContextFactory;
        _vehicleFilterOptionsCache = vehicleFilterOptionsCache;
        _httpClientFactory = httpClientFactory;
        _aiQuickEvaluationAdminOnly = configuration.GetValue("Features:AiQuickEvaluationAdminOnly", true);
    }

    [HttpGet("list")]
    public async Task<IActionResult> GetLots(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string[]? categories = null,
        [FromQuery] string? searchQuery = null,
        [FromQuery] string? biddingType = null,
        [FromQuery] decimal? priceFrom = null,
        [FromQuery] decimal? priceTo = null,
        [FromQuery] bool? isSharedOwnership = null,
        [FromQuery] string[]? regions = null,
        [FromQuery] bool onlyActive = true)
    {
        // Извлекаем динамические фильтры из Query (все, что начинается с attr_)
        var dynamicFilters = new Dictionary<string, string>();
        foreach (var key in Request.Query.Keys)
        {
            if (key.StartsWith("attr_"))
            {
                var attrName = key.Substring(5); // Убираем префикс "attr_"
                dynamicFilters[attrName] = Request.Query[key].ToString();
            }
        }

        var spec = new LotsListSpecification(
            page, pageSize, categories, searchQuery, biddingType, priceFrom, priceTo, isSharedOwnership, regions, onlyActive, dynamicFilters);

        var filterSpec = new LotsFilterSpecification(
            categories, searchQuery, biddingType, priceFrom, priceTo, isSharedOwnership, regions, onlyActive, dynamicFilters);

        // Два отдельных DbContext: один контекст нельзя использовать параллельно
        await using var countContext = await _dbContextFactory.CreateDbContextAsync();
        await using var listContext = await _dbContextFactory.CreateDbContextAsync();

        var countTask = countContext.Lots.WithSpecification(filterSpec).CountAsync();
        var listTask = listContext.Lots.WithSpecification(spec).ToListAsync();
        await Task.WhenAll(countTask, listTask);

        var totalCount = await countTask;
        var lots = await listTask;

        var lotDtos = lots.Select(l => new LotDto
        {
            Id = l.Id,
            PublicId = l.PublicId,
            LotNumber = l.LotNumber,
            StartPrice = l.StartPrice,
            Step = l.Step,
            Deposit = l.Deposit,
            TradeStatus = l.TradeStatus,
            FinalPrice = l.FinalPrice,
            Title = l.Title ?? l.Description,
            Slug = l.Slug,
            ViewCount = l.ViewCount,
            VotesCount = l.VotesCount,
            Description = l.Description,
            ViewingProcedure = l.ViewingProcedure,
            CreatedAt = l.CreatedAt,
            Coordinates = (l.Latitude.HasValue && l.Longitude.HasValue)
                ? new[] { l.Latitude.Value, l.Longitude.Value }
                : null,
            PropertyRegionName = l.PropertyRegionName,
            MarketValue = l.MarketValue,
            MarketValueMin = l.MarketValueMin,
            MarketValueMax = l.MarketValueMax,
            PriceConfidence = l.PriceConfidence,
            InvestmentSummary = l.InvestmentSummary,
            Bidding = new BiddingDto
            {
                Type = l.Bidding.Type,
                ViewingProcedure = l.Bidding.ViewingProcedure
            },
            Categories = l.Categories.Select(c => new CategoryDto
            {
                Id = c.Id,
                Name = c.Name
            }).ToList(),
            Attributes = l.Attributes,
            PriceSchedules = l.PriceSchedules
                .OrderBy(ps => ps.StartDate)
                .Select((ps, index) => new PriceScheduleDto
                {
                    Number = index + 1,
                    StartDate = ps.StartDate,
                    EndDate = ps.EndDate,
                    Price = ps.Price
                }),
            Images = l.Images
                .OrderBy(i => i.Order)
                .Select(i => i.Url)
                .ToList()
        }).ToList();

        if (_aiQuickEvaluationAdminOnly)
        {
            var showAiEvaluation = await IsAdminAsync();
            LotDtoAiEvaluationAccess.ApplyQuickEvaluationVisibility(lotDtos, showAiEvaluation);
        }

        var result = new PaginatedResult<LotDto>(lotDtos, totalCount, page, pageSize);

        return Ok(result);
    }

    /// <summary>
    /// Справочник марок и моделей для фильтра «Легковой автомобиль» (из in-memory кэша).
    /// </summary>
    [HttpGet("vehicle-filter-options")]
    public IActionResult GetVehicleFilterOptions()
    {
        return Ok(_vehicleFilterOptionsCache.Get());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetLotAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { message = "Некорректный ID лота." });
        }

        ISingleResultSpecification<Lot>? spec = null;

        if (int.TryParse(id, out int publicId))
        {
            spec = new LotByIdWithDetailsSpecification(publicId);
        }
        else if (Guid.TryParse(id, out Guid guidId))
        {
            spec = new LotByIdWithDetailsSpecification(guidId);
        }
        else
        {
            // Если это не число и не GUID (например, "some-slug-123" без правильной обработки на фронте)
            return BadRequest(new { message = "Неверный формат ID." });
        }

        var lot = await _dbContext.Lots.WithSpecification(spec).FirstOrDefaultAsync();

        if (lot == null)
        {
            return NotFound(new { message = "Лот не найден." });
        }

        // Увеличиваем счетчик просмотров только для обычных пользователей
        if (!await IsAdminAsync())
        {
            await _dbContext.Lots
                .Where(l => l.Id == lot.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.ViewCount, l => l.ViewCount + 1));

            // Обновляем значение в памяти, чтобы вернуть актуальное
            lot.ViewCount++;
        }

        var normalizedLotNumber = TradeResultsScheduleHelper.NormalizeLotNumber(lot.LotNumber);
        var lotTradeResults = string.IsNullOrWhiteSpace(normalizedLotNumber)
            ? []
            : await _dbContext.LotTradeResults
                .AsNoTracking()
                .Where(r => r.BiddingId == lot.BiddingId && r.LotNumber == normalizedLotNumber)
                .ToListAsync();

        var tradeStatusReason = TradeResultsScheduleHelper.GetLatestReasonForLot(lot, lotTradeResults);

        var evaluation = await _dbContext.LotEvaluations
            .AsNoTracking()
            .Where(e => e.LotId == lot.Id)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();

        string? reasoningText = null;
        bool isReasoningTextTeaser = false;

        if (evaluation != null && !string.IsNullOrWhiteSpace(evaluation.ReasoningText))
        {
            if (!lot.IsActive())
            {
                // Лот в архиве - отдаем полный текст для SEO
                reasoningText = evaluation.ReasoningText;
                isReasoningTextTeaser = false;
            }
            else
            {
                // Лот активен - отдаем тизер (70% текста)
                int totalLength = evaluation.ReasoningText.Length;
                int teaserLength = (int)(totalLength * 0.7);
                
                // Чтобы не обрезать на полуслове, найдем ближайший пробел или перенос строки
                // после отметки в 70% (или до нее, если текст короткий)
                if (teaserLength < totalLength)
                {
                    reasoningText = evaluation.ReasoningText.Substring(0, teaserLength) + "...";
                    isReasoningTextTeaser = true;
                }
                else
                {
                    reasoningText = evaluation.ReasoningText;
                    isReasoningTextTeaser = false;
                }
            }
        }

        var lotDto = new LotDto
        {
            Id = lot.Id,
            PublicId = lot.PublicId,
            LotNumber = lot.LotNumber,
            StartPrice = lot.StartPrice,
            Step = lot.Step,
            Deposit = lot.Deposit,
            TradeStatus = lot.TradeStatus,
            TradeStatusReason = tradeStatusReason,
            FinalPrice = lot.FinalPrice,
            WinnerName = lot.WinnerName,
            WinnerInn = lot.WinnerInn,
            Title = lot.Title,
            Slug = lot.Slug,
            ViewCount = lot.ViewCount,
            VotesCount = lot.VotesCount,
            Description = lot.Description,
            ViewingProcedure = lot.ViewingProcedure,
            CreatedAt = lot.CreatedAt,
            Coordinates = (lot.Latitude.HasValue && lot.Longitude.HasValue)
                ? [lot.Latitude.Value, lot.Longitude.Value]
                : null,
            PropertyRegionName = lot.PropertyRegionName,
            PropertyFullAddress = lot.PropertyFullAddress,
            MarketValue = lot.MarketValue,
            MarketValueMin = lot.MarketValueMin,
            MarketValueMax = lot.MarketValueMax,
            PriceConfidence = lot.PriceConfidence,
            InvestmentSummary = lot.InvestmentSummary,
            ReasoningText = reasoningText,
            IsReasoningTextTeaser = isReasoningTextTeaser,
            LiquidityScore = evaluation?.LiquidityScore,
            Attributes = lot.Attributes,
            NeedsDescriptionReview = lot.NeedsDescriptionReview,

            CadastralInfos = lot.CadastralInfos?.Select(c => new CadastralItemDto
            {
                CadastralNumber = c.CadastralNumber,
                Area = c.Area,
                CadastralCost = c.CadastralCost,
                Category = c.Category,
                PermittedUse = c.PermittedUse,
                Address = c.Address,
                Status = c.Status,
                ObjectType = c.ObjectType,
                RightType = c.RightType,
                OwnershipType = c.OwnershipType,
                RegDate = c.RegDate
            }).ToList(),

            Bidding = new BiddingDto
            {
                Id = lot.Bidding.Id,
                Type = lot.Bidding.Type,
                Platform = PlatformDisplayName.GetDisplayName(lot.Bidding.Platform),
                TradeNumber = lot.Bidding.TradeNumber,
                BankruptMessageId = lot.Bidding.BankruptMessageId,
                BidAcceptancePeriod = lot.Bidding.BidAcceptancePeriod,
                TradePeriod = lot.Bidding.TradePeriod,
                ResultsAnnouncementDate = lot.Bidding.ResultsAnnouncementDate,
                ViewingProcedure = lot.Bidding.ViewingProcedure,
                ArbitrationManager = lot.Bidding.ArbitrationManager != null
                    ? new ArbitrationManagerDto
                    {
                        Name = lot.Bidding.ArbitrationManager.Name,
                        Inn = lot.Bidding.ArbitrationManager.Inn,
                        Snils = lot.Bidding.ArbitrationManager.Snils,
                        Ogrn = lot.Bidding.ArbitrationManager.Ogrn
                    }
                    : null,
                Debtor = lot.Bidding.Debtor != null
                    ? new DebtorDto
                    {
                        Name = lot.Bidding.Debtor.Name,
                        Inn = lot.Bidding.Debtor.Inn,
                        Snils = lot.Bidding.Debtor.Snils,
                        Ogrn = lot.Bidding.Debtor.Ogrn
                    }
                    : null
            },
            Categories = lot.Categories.Select(c => new CategoryDto
            {
                Id = c.Id,
                Name = c.Name
            }).ToList(),

            PriceSchedules = lot.PriceSchedules
                .OrderBy(ps => ps.StartDate)
                .Select((ps, index) => new PriceScheduleDto
                {
                    Number = index + 1,
                    StartDate = ps.StartDate,
                    EndDate = ps.EndDate,
                    Price = ps.Price,
                    Deposit = ps.Deposit,
                    EstimatedRank = ps.EstimatedRank,
                    PotentialRoi = ps.PotentialRoi
                }).ToList(),

            Images = lot.Images
                .OrderBy(i => i.Order)
                .Select(i => i.Url)
                .ToList(),

            Documents = lot.Documents
                .Select(d => MapDocumentDto(lot.PublicId, d))
                .ToList()
        };

        if (!lot.IsActive())
        {
            var similarLots = await _dbContext.SimilarLots
                .AsNoTracking()
                .Where(sl => sl.SourceLotId == lot.Id)
                .OrderBy(sl => sl.Rank)
                .Include(sl => sl.TargetLot)
                .ThenInclude(l => l.Images)
                .ToListAsync();

            lotDto.SimilarLots = similarLots.Select(sl => new SimilarLotDto
            {
                Id = sl.TargetLot.Id,
                PublicId = sl.TargetLot.PublicId,
                Title = sl.TargetLot.Title ?? sl.TargetLot.Description,
                Slug = sl.TargetLot.Slug,
                StartPrice = sl.TargetLot.StartPrice,
                ImageUrl = sl.TargetLot.Images.OrderBy(i => i.Order).FirstOrDefault()?.Url
            }).ToList();

            // Если есть кадастровые номера, ищем активные лоты с такими же номерами
            if (lot.CadastralInfos != null && lot.CadastralInfos.Any())
            {
                var cadastralNumbers = lot.CadastralInfos.Select(c => c.CadastralNumber).ToList();
                
                var lotsWithSameCadastral = await _dbContext.Lots
                    .AsNoTracking()
                    .Where(Lot.IsActiveExpression)
                    .Where(l => l.Id != lot.Id)
                    .Where(l => l.CadastralInfos.Any(c => cadastralNumbers.Contains(c.CadastralNumber)))
                    .Include(l => l.Images)
                    .OrderByDescending(l => l.CreatedAt)
                    .Take(5)
                    .ToListAsync();

                if (lotsWithSameCadastral.Any())
                {
                    lotDto.SameCadastralLots = lotsWithSameCadastral.Select(l => new SimilarLotDto
                    {
                        Id = l.Id,
                        PublicId = l.PublicId,
                        Title = l.Title ?? l.Description,
                        Slug = l.Slug,
                        StartPrice = l.StartPrice,
                        ImageUrl = l.Images.OrderBy(i => i.Order).FirstOrDefault()?.Url
                    }).ToList();

                    // Убираем их из SimilarLots, чтобы не было дублей
                    var existingIds = new HashSet<Guid>(lotDto.SameCadastralLots.Select(s => s.Id));
                    lotDto.SimilarLots = lotDto.SimilarLots.Where(s => !existingIds.Contains(s.Id)).ToList();
                }
            }
        }

        if (_aiQuickEvaluationAdminOnly)
        {
            var showAiEvaluation = await IsAdminAsync();
            LotDtoAiEvaluationAccess.ApplyQuickEvaluationVisibility(lotDto, showAiEvaluation);
        }

        return Ok(lotDto);
    }

    /// <summary>
    /// Получает список лотов с координатами для отображения на интерактивной карте.
    /// </summary>
    /// <remarks>
    /// Поддерживает фильтрацию по Bounding Box (видимой области экрана). 
    /// Для пользователей с подпиской PRO (Full Access) возвращает все объекты в рамках экрана (с лимитом для защиты от перегрузки).
    /// Для пользователей без активной подписки возвращает лоты только из ограниченного глобального демо-пула (Teaser Set), 
    /// но при этом рассчитывает реальное количество объектов в видимой зоне для маркетинговой плашки.
    /// </remarks>
    [HttpGet("with-coordinates")]
    public async Task<IActionResult> GetLotsWithCoordinates(
        [FromQuery] string[]? categories = null,
        [FromQuery] bool onlyActive = true,
        [FromQuery] double? minLat = null,
        [FromQuery] double? maxLat = null,
        [FromQuery] double? minLon = null,
        [FromQuery] double? maxLon = null)
    {
        AccessLevel accessLevel = AccessLevel.Anonymous;

        // Проверяем, аутентифицирован ли пользователь
        if (User.Identity?.IsAuthenticated == true)
        {
            // Получаем ID пользователя из токена
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userIdString, out var userId))
            {
                var user = await _dbContext.Users.FindAsync(userId);
                if (user == null)
                {
                    return Unauthorized("Пользователь не найден");
                }

                // Проверяем, активна ли подписка
                if (user.IsSubscriptionActive &&
                    user.SubscriptionEndDate.HasValue &&
                    user.SubscriptionEndDate.Value > DateTime.UtcNow)
                {
                    accessLevel = AccessLevel.Full;
                }
                else
                {
                    accessLevel = AccessLevel.Limited;
                }
            }
        }

        var spec = new LotsWithCoordinatesSpecification(categories, onlyActive);

        // Базовый запрос ко всем лотам (до применения фильтра по карте)
        var baseQuery = _dbContext.Lots.WithSpecification(spec);

        // Запрос для конкретной области (Bounding Box)
        var bboxQuery = baseQuery;
        if (minLat.HasValue && maxLat.HasValue && minLon.HasValue && maxLon.HasValue)
        {
            bboxQuery = bboxQuery.Where(l =>
                l.Latitude >= minLat.Value && l.Latitude <= maxLat.Value &&
                l.Longitude >= minLon.Value && l.Longitude <= maxLon.Value);
        }

        // Считаем реальное количество объектов в видимой зоне (для маркетинговой плашки)
        var totalCountInBBox = await bboxQuery.CountAsync();

        List<LotGeoResult> dbResults;

        // Выборка данных в зависимости от прав доступа
        if (accessLevel == AccessLevel.Full)
        {
            // PRO-пользователь: отдаем все лоты в видимой области (с защитным лимитом от зависания браузера)
            dbResults = await bboxQuery.Take(2000).ToListAsync();
        }
        else
        {
            // БЕСПЛАТНЫЙ пользователь: 
            // Создаем "глобальный демо-пул" (например, 200 лотов со всей страны).
            // Обязательно делаем OrderBy, чтобы набор демо-лотов был всегда стабильным!
            var globalFreePool = baseQuery
                .OrderByDescending(l => l.Id) // Или l.PublishedDate
                .Take(200);

            // Теперь применяем фильтр по экрану только к этому маленькому демо-пулу
            if (minLat.HasValue && maxLat.HasValue && minLon.HasValue && maxLon.HasValue)
            {
                dbResults = await globalFreePool.Where(l =>
                    l.Latitude >= minLat.Value && l.Latitude <= maxLat.Value &&
                    l.Longitude >= minLon.Value && l.Longitude <= maxLon.Value)
                    .ToListAsync();
            }
            else
            {
                dbResults = await globalFreePool.ToListAsync();
            }
        }

        var lotsForMap = dbResults
            .Select(r => new LotGeoDto
            {
                Id = r.Id,
                Type = r.Type,
                Title = r.Title,
                StartPrice = r.StartPrice,
                Latitude = r.Latitude,
                Longitude = r.Longitude,
            }).ToList();

        var response = new MapLotsResponse
        {
            Lots = lotsForMap,
            TotalCount = totalCountInBBox, // Отправляем реальное количество объектов в этой зоне
            AccessLevel = accessLevel
        };

        return Ok(response);
    }

    [HttpPost("{lotId:guid}/copy-to-prod")]
    public async Task<IActionResult> CopyToProd(Guid lotId)
    {
        if (lotId == Guid.Empty)
        {
            return BadRequest("Некорректный ID лота.");
        }

        var success = await _lotCopyService.CopyLotToProdAsync(lotId);

        if (success)
        {
            return Ok(new { message = "Лот успешно скопирован." });
        }

        return StatusCode(500, "Произошла ошибка при копировании лота.");
    }

    [HttpGet("all-ids")]
    [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllIds()
    {
        var ids = await _dbContext.Lots.Select(l => l.Id).ToListAsync();
        return Ok(ids);
    }

    [HttpGet("popular")]
    public async Task<IActionResult> GetPopularLots([FromQuery] int limit = 20)
    {
        var popularLots = await _dbContext.Lots
            .AsNoTracking()
            .Where(Lot.IsActiveExpression)
            .OrderByDescending(l => l.VotesCount)
            .ThenByDescending(l => l.ViewCount)
            .Take(limit)
            .Select(l => new
            {
                l.Id,
                l.PublicId,
                l.Title,
                l.Slug,
                l.ViewCount,
                l.VotesCount,
                HasEvaluation = _dbContext.LotEvaluations.Any(e => e.LotId == l.Id)
            })
            .ToListAsync();

        return Ok(popularLots);
    }

    [HttpGet("sitemap-data")]
    public async Task<IActionResult> GetSitemapData(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 40000)
    {
        if (pageSize > 50000)
        {
            pageSize = 50000;
        }

        var query = _dbContext.Lots.AsNoTracking();

        var items = await query
            .Where(Lot.IsActiveExpression) // Оставляем только активные лоты
            .Where(l => string.IsNullOrEmpty(l.Title))
            .OrderByDescending(l => l.CreatedAt) // Свежие сначала
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new SitemapItemDto
            {
                PublicId = l.PublicId,
                Slug = l.Slug, // Обязательно передаем Slug из БД
                Title = l.Title!,
                Description = l.Description, // Для фоллбэка генерации
                CreatedAt = l.CreatedAt
            })
            .ToListAsync();

        return Ok(items);
    }

    private async Task<bool> IsAdminAsync() =>
        await AdminAccessHelper.IsAdminAsync(HttpContext, _dbContext);

    public class UpdateLotDescriptionRequest
    {
        public string Description { get; set; } = default!;
    }

    [Authorize]
    [HttpPut("{id}/description")]
    public async Task<IActionResult> UpdateDescription(string id, [FromBody] UpdateLotDescriptionRequest request)
    {
        if (!await IsAdminAsync()) return Forbid();

        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { message = "Некорректный ID лота." });

        Lot? lot = null;
        if (int.TryParse(id, out int publicId))
        {
            lot = await _dbContext.Lots.FirstOrDefaultAsync(l => l.PublicId == publicId);
        }
        else if (Guid.TryParse(id, out Guid guidId))
        {
            lot = await _dbContext.Lots.FirstOrDefaultAsync(l => l.Id == guidId);
        }

        if (lot == null)
            return NotFound(new { message = "Лот не найден." });

        lot.Description = request.Description;
        lot.NeedsDescriptionReview = false;

        var classificationState = await _dbContext.LotClassificationStates.FirstOrDefaultAsync(s => s.LotId == lot.Id);
        if (classificationState == null)
        {
            classificationState = new LotClassificationState
            {
                LotId = lot.Id,
                Status = ClassificationStatus.Pending,
                Attempts = 0,
                NextAttemptAt = DateTime.UtcNow
            };
            _dbContext.LotClassificationStates.Add(classificationState);
        }
        else
        {
            classificationState.Status = ClassificationStatus.Pending;
            classificationState.Attempts = 0;
            classificationState.NextAttemptAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Описание успешно обновлено." });
    }

    public class UpdateViewingProcedureRequest
    {
        public string? ViewingProcedure { get; set; }
    }

    [Authorize]
    [HttpPut("{id}/viewing-procedure")]
    public async Task<IActionResult> UpdateViewingProcedure(string id, [FromBody] UpdateViewingProcedureRequest request)
    {
        if (!await IsAdminAsync()) return Forbid();

        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { message = "Некорректный ID лота." });

        Lot? lot = null;
        if (int.TryParse(id, out int publicId))
        {
            lot = await _dbContext.Lots.Include(l => l.Bidding).FirstOrDefaultAsync(l => l.PublicId == publicId);
        }
        else if (Guid.TryParse(id, out Guid guidId))
        {
            lot = await _dbContext.Lots.Include(l => l.Bidding).FirstOrDefaultAsync(l => l.Id == guidId);
        }

        if (lot == null)
            return NotFound(new { message = "Лот не найден." });

        if (lot.Bidding == null)
            return BadRequest(new { message = "Торги для лота не найдены." });

        lot.Bidding.ViewingProcedure = string.IsNullOrWhiteSpace(request.ViewingProcedure)
            ? null
            : request.ViewingProcedure.Trim();

        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Порядок ознакомления успешно обновлён." });
    }

    [Authorize]
    [HttpPost("{id}/images")]
    public async Task<IActionResult> UploadImages(string id, List<IFormFile> files, [FromServices] ILotsFileStorageService storageService)
    {
        if (!await IsAdminAsync()) return Forbid();

        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { message = "Некорректный ID лота." });

        Lot? lot = null;
        if (int.TryParse(id, out int publicId))
        {
            lot = await _dbContext.Lots.Include(l => l.Images).FirstOrDefaultAsync(l => l.PublicId == publicId);
        }
        else if (Guid.TryParse(id, out Guid guidId))
        {
            lot = await _dbContext.Lots.Include(l => l.Images).FirstOrDefaultAsync(l => l.Id == guidId);
        }

        if (lot == null)
            return NotFound(new { message = "Лот не найден." });

        if (files == null || files.Count == 0)
            return BadRequest(new { message = "Файлы не выбраны." });

        var maxOrder = lot.Images.Any() ? lot.Images.Max(i => i.Order) : 0;
        var uploadedImages = new List<object>();

        foreach (var file in files)
        {
            if (file.Length == 0) continue;

            var extension = Path.GetExtension(file.FileName);
            var fileName = $"lots/{lot.Id}/{Guid.NewGuid()}{extension}";

            using var stream = file.OpenReadStream();
            var url = await storageService.UploadAsync(stream, fileName, file.ContentType);

            maxOrder++;
            var lotImage = new LotImage
            {
                LotId = lot.Id,
                Url = url,
                Order = maxOrder
            };

            _dbContext.Set<LotImage>().Add(lotImage);
            uploadedImages.Add(new { url = url, order = lotImage.Order });
        }

        await _dbContext.SaveChangesAsync();

        return Ok(uploadedImages);
    }

    [HttpGet("{id}/documents/{documentId:guid}/download")]
    public async Task<IActionResult> DownloadDocument(
        string id,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        Lot? lot = null;
        if (int.TryParse(id, out int publicId))
        {
            lot = await _dbContext.Lots
                .AsNoTracking()
                .Include(l => l.Bidding)
                .FirstOrDefaultAsync(l => l.PublicId == publicId, cancellationToken);
        }
        else if (Guid.TryParse(id, out Guid guidId))
        {
            lot = await _dbContext.Lots
                .AsNoTracking()
                .Include(l => l.Bidding)
                .FirstOrDefaultAsync(l => l.Id == guidId, cancellationToken);
        }

        if (lot == null)
            return NotFound(new { message = "Лот не найден." });

        var document = await _dbContext.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.LotId == lot.Id, cancellationToken);

        if (document == null)
            return NotFound(new { message = "Документ не найден." });

        if (!document.IsExternal)
        {
            if (string.IsNullOrWhiteSpace(document.Url))
                return NotFound(new { message = "Файл документа недоступен." });

            return Redirect(document.Url);
        }

        var referer = lot.Bidding?.BankruptMessageId != null && lot.Bidding.BankruptMessageId != Guid.Empty
            ? $"https://fedresurs.ru/bankruptmessages/{lot.Bidding.BankruptMessageId}"
            : "https://fedresurs.ru/";

        var client = _httpClientFactory.CreateClient("FedresursDownload");
        using var request = new HttpRequestMessage(HttpMethod.Get, document.SourceUrl);
        request.Headers.TryAddWithoutValidation("Referer", referer);

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, new { message = "Не удалось скачать документ с Федресурса." });

        var contentType = response.Content.Headers.ContentType?.MediaType
            ?? LotPropertyDocumentHelper.GetContentType(document.Extension ?? ".bin");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        
        var fileName = document.Title;
        if (!string.IsNullOrWhiteSpace(document.Extension) &&
            !fileName.EndsWith(document.Extension, StringComparison.OrdinalIgnoreCase))
        {
            fileName += document.Extension;
        }

        return File(bytes, contentType, fileName);
    }

    [Authorize]
    [HttpPost("{id}/documents")]
    public async Task<IActionResult> UploadDocuments(
        string id,
        List<IFormFile> files,
        [FromQuery] bool extractToDescription = false,
        [FromServices] ILotsFileStorageService storageService = null!,
        [FromServices] Lots.Application.Services.DebtScoring.IDocumentTextExtractor textExtractor = null!,
        [FromServices] ILotPropertyDescriptionSummarizer descriptionSummarizer = null!)
    {
        if (!await IsAdminAsync()) return Forbid();

        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { message = "Некорректный ID лота." });

        Lot? lot = null;
        if (int.TryParse(id, out int publicId))
        {
            lot = await _dbContext.Lots.Include(l => l.Documents).Include(l => l.Bidding).FirstOrDefaultAsync(l => l.PublicId == publicId);
        }
        else if (Guid.TryParse(id, out Guid guidId))
        {
            lot = await _dbContext.Lots.Include(l => l.Documents).Include(l => l.Bidding).FirstOrDefaultAsync(l => l.Id == guidId);
        }

        if (lot == null)
            return NotFound(new { message = "Лот не найден." });

        if (files == null || files.Count == 0)
            return BadRequest(new { message = "Файлы не выбраны." });

        var uploadedDocuments = new List<object>();
        var extractedTexts = new List<string>();

        foreach (var file in files)
        {
            if (file.Length == 0) continue;

            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension) ||
                !LotPropertyDocumentHelper.PropertyDocumentExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = $"Неподдерживаемый формат файла: {extension}" });
            }

            using var stream = file.OpenReadStream();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var bytes = memory.ToArray();

            var fileName = $"lots/{lot.Id}/documents/{Guid.NewGuid()}{extension.ToLowerInvariant()}";
            var url = await storageService.UploadAsync(
                bytes,
                fileName,
                LotPropertyDocumentHelper.GetContentType(extension));

            var title = Path.GetFileNameWithoutExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(title))
                title = "Документ";

            var lotDocument = new LotDocument
            {
                Id = Guid.NewGuid(),
                LotId = lot.Id,
                Url = url,
                Title = title,
                Extension = extension,
                CreatedAt = DateTime.UtcNow,
            };

            _dbContext.Documents.Add(lotDocument);
            uploadedDocuments.Add(new
            {
                id = lotDocument.Id,
                downloadUrl = MapDocumentDto(lot.PublicId, lotDocument).DownloadUrl,
                title = lotDocument.Title,
                extension = lotDocument.Extension,
            });

            if (extractToDescription && textExtractor.CanExtract(extension))
            {
                var extraction = await textExtractor.ExtractAsync(bytes, extension);
                if (extraction.Success && !string.IsNullOrWhiteSpace(extraction.Text))
                {
                    var rawText = extraction.Text.Trim();
                    var documentType = LotPropertyDocumentHelper.DetermineDocumentType(title, rawText);

                    // Контракты и неизвестные типы не обобщаем и не подмешиваем в описание автоматически.
                    if (documentType == PropertyDocumentType.PropertyList)
                    {
                        if (LotPropertyDocumentHelper.ShouldSummarizeForDescription(documentType, rawText))
                        {
                            var summary = await descriptionSummarizer.SummarizeAsync(rawText);
                            extractedTexts.Add(
                                !string.IsNullOrWhiteSpace(summary.Summary)
                                    ? summary.Summary
                                    : LotPropertyDocumentHelper.TruncateForPreview(rawText, 2000));
                        }
                        else
                        {
                            extractedTexts.Add(rawText);
                        }
                    }
                }
            }
        }

        string? mergedDescription = null;
        if (extractToDescription && extractedTexts.Count > 0)
        {
            mergedDescription = LotPropertyDocumentHelper.BuildProposedDescription(lot.Description, string.Join("\n\n", extractedTexts));
            if (!string.IsNullOrWhiteSpace(mergedDescription))
            {
                if (LotPropertyDocumentHelper.IsPropertyListReferral(lot.Description) && lot.Bidding != null)
                {
                    lot.Bidding.ViewingProcedure = LotDescriptionTextHelper.MergeViewingProcedureParts(
                        lot.Bidding.ViewingProcedure,
                        lot.Description);
                }

                lot.Description = mergedDescription;
                lot.NeedsDescriptionReview = false;
                lot.Slug = null;

                var classificationState = await _dbContext.LotClassificationStates.FirstOrDefaultAsync(s => s.LotId == lot.Id);
                if (classificationState == null)
                {
                    _dbContext.LotClassificationStates.Add(new LotClassificationState
                    {
                        LotId = lot.Id,
                        Status = ClassificationStatus.Pending,
                        Attempts = 0,
                        NextAttemptAt = DateTime.UtcNow,
                    });
                }
                else
                {
                    classificationState.Status = ClassificationStatus.Pending;
                    classificationState.Attempts = 0;
                    classificationState.NextAttemptAt = DateTime.UtcNow;
                }
            }
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            documents = uploadedDocuments,
            extractedDescription = mergedDescription,
        });
    }

    [Authorize]
    [HttpPost("{id}/reclassify")]
    public async Task<IActionResult> ReclassifyLot(string id)
    {
        if (!await IsAdminAsync()) return Forbid();

        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { message = "Некорректный ID лота." });

        Lot? lot = null;
        if (int.TryParse(id, out int publicId))
        {
            lot = await _dbContext.Lots.FirstOrDefaultAsync(l => l.PublicId == publicId);
        }
        else if (Guid.TryParse(id, out Guid guidId))
        {
            lot = await _dbContext.Lots.FirstOrDefaultAsync(l => l.Id == guidId);
        }

        if (lot == null)
            return NotFound(new { message = "Лот не найден." });

        lot.Slug = null;

        var classificationState = await _dbContext.LotClassificationStates.FirstOrDefaultAsync(s => s.LotId == lot.Id);
        if (classificationState == null)
        {
            classificationState = new LotClassificationState
            {
                LotId = lot.Id,
                Status = ClassificationStatus.Pending,
                Attempts = 0,
                NextAttemptAt = DateTime.UtcNow
            };
            _dbContext.LotClassificationStates.Add(classificationState);
        }
        else
        {
            classificationState.Status = ClassificationStatus.Pending;
            classificationState.Attempts = 0;
            classificationState.NextAttemptAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Лот поставлен в очередь на переклассификацию." });
    }

    private static LotDocumentDto MapDocumentDto(int publicId, LotDocument document) =>
        new()
        {
            Id = document.Id,
            Title = document.Title,
            Extension = document.Extension,
            IsExternal = document.IsExternal,
            DownloadUrl = document.IsExternal
                ? LotDocumentLinkHelper.BuildDownloadApiPath(publicId, document.Id)
                : document.Url ?? string.Empty,
        };
}
