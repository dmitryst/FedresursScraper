using Microsoft.AspNetCore.Mvc;
using FedresursScraper.Services;
using Lots.Data.Specifications;
using Microsoft.EntityFrameworkCore;
using FedresursScraper.Controllers.Models;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Lots.Data.Entities;
using Ardalis.Specification;
using Lots.Data.Models;


namespace FedresursScraper.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LotsController : ControllerBase
{
    private readonly ILotCopyService _lotCopyService;
    private readonly LotsDbContext _dbContext;

    public LotsController(
        ILotCopyService lotCopyService,
        LotsDbContext dbContext)
    {
        _lotCopyService = lotCopyService;
        _dbContext = dbContext;
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
        var spec = new LotsListSpecification(
            page, pageSize, categories, searchQuery, biddingType, priceFrom, priceTo, isSharedOwnership, regions, onlyActive);

        var filterSpec = new LotsFilterSpecification(
            categories, searchQuery, biddingType, priceFrom, priceTo, isSharedOwnership, regions, onlyActive);

        var totalCount = await _dbContext.Lots.WithSpecification(filterSpec).CountAsync();

        var lots = await _dbContext.Lots.WithSpecification(spec).ToListAsync();

        var lotDtos = lots.Select(l => new LotDto
        {
            Id = l.Id,
            PublicId = l.PublicId,
            LotNumber = l.LotNumber,
            StartPrice = l.StartPrice,
            Step = l.Step,
            Deposit = l.Deposit,
            Title = l.Title ?? l.Description,
            Slug = l.Slug,
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

            Images = l.Images
                .OrderBy(i => i.Order)
                .Select(i => i.Url)
                .ToList()
        }).ToList();

        var result = new PaginatedResult<LotDto>(lotDtos, totalCount, page, pageSize);

        return Ok(result);
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

        var lotDto = new LotDto
        {
            Id = lot.Id,
            PublicId = lot.PublicId,
            LotNumber = lot.LotNumber,
            StartPrice = lot.StartPrice,
            Step = lot.Step,
            Deposit = lot.Deposit,
            TradeStatus = lot.TradeStatus,
            FinalPrice = lot.FinalPrice,
            WinnerName = lot.WinnerName,
            WinnerInn = lot.WinnerInn,
            Title = lot.Title,
            Slug = lot.Slug,
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
                Type = lot.Bidding.Type,
                Platform = lot.Bidding.Platform,
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
                .Select(d => new LotDocumentDto
                {
                    Id = d.Id,
                    Url = d.Url,
                    Title = d.Title,
                    Extension = d.Extension
                }).ToList(),
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

    private async Task<bool> IsAdminAsync()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdString, out Guid userId)) return false;

        var user = await _dbContext.Users.FindAsync(userId);
        return user?.IsAdmin == true;
    }

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
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Описание успешно обновлено." });
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
}
