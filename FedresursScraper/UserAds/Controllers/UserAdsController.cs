using System.Globalization;
using FedresursScraper.Services;
using Lots.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FedresursScraper.UserAds;

[ApiController]
[Route("api/ads")]
public class UserAdsController : ControllerBase
{
    private readonly LotsDbContext _dbContext;
    private readonly IUserAdsFileStorageService _fileStorage;

    public UserAdsController(LotsDbContext dbContext, IUserAdsFileStorageService fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    /// <summary>
    /// Создание нового объявления пользователем
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateAd([FromForm] CreateUserAdDto dto)
    {
        // Получаем ID текущего пользователя из токена/куки
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var userId))
            return Unauthorized();

        // Создаем сущность
        var ad = new UserAd
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = dto.Title,
            Description = dto.Description,
            Price = dto.Price,
            Region = dto.Region,
            Category = dto.Category,
            Latitude = dto.Latitude != null ? double.Parse(dto.Latitude.Replace(',', '.'), CultureInfo.InvariantCulture) : null,
            Longitude = dto.Longitude != null ? double.Parse(dto.Longitude.Replace(',', '.'), CultureInfo.InvariantCulture) : null,
            Status = AdStatus.UnderModeration,
            CreatedAt = DateTime.UtcNow
        };

        // Сохраняем картинки
        if (dto.Images != null && dto.Images.Any())
        {
            var order = 1;

            foreach (var file in dto.Images.Take(10)) // Ограничиваем 10 фото
            {
                // Проверка на пустой файл
                if (file.Length == 0) continue;

                // Проверяем, что это картинка
                if (!file.ContentType.StartsWith("image/")) continue;

                var guid = Guid.NewGuid();
                var fileName = $"ads/{ad.Id}/{guid}.jpg";   // Используем ad.Id для папки

                // Открываем поток напрямую из загруженного файла
                using var stream = file.OpenReadStream();

                // Передаем поток в сервис
                var s3Url = await _fileStorage.UploadAsync(stream, fileName, file.ContentType);

                ad.Images.Add(new UserAdImage
                {
                    Id = guid,
                    Url = s3Url,
                    IsMain = order == 1, // Первое фото делаем главным
                    Order = order++
                });
            }
        }

        _dbContext.UserAds.Add(ad);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAdById), new { id = ad.Id }, ad.Id);
    }

    /// <summary>
    /// Получение ленты объявлений для всех (каталог)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetActiveAds([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = _dbContext.UserAds
            .Include(a => a.Images)
            .Where(a => a.Status == AdStatus.Active)
            .OrderByDescending(a => a.CreatedAt);

        var total = await query.CountAsync();

        var ads = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new UserAdDto
            {
                Id = a.Id,
                Title = a.Title,
                Description = a.Description,
                Price = a.Price,
                Region = a.Region,
                Category = a.Category,
                Latitude = a.Latitude,
                Longitude = a.Longitude,
                CreatedAt = a.CreatedAt,
                Status = (int)a.Status,
                ImageUrls = a.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList()
            })
            .AsNoTracking()
            .ToListAsync();

        return Ok(new { Total = total, Ads = ads });
    }

    /// <summary>
    /// Получение объявлений текущего пользователя
    /// </summary>
    [HttpGet("my")]
    [Authorize]
    public async Task<IActionResult> GetMyAds()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var userId))
            return Unauthorized();

        var ads = await _dbContext.UserAds
            .Include(a => a.Images)
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new UserAdDto
            {
                Id = a.Id,
                Title = a.Title,
                Description = a.Description,
                Price = a.Price,
                Region = a.Region,
                Category = a.Category,
                Latitude = a.Latitude,
                Longitude = a.Longitude,
                CreatedAt = a.CreatedAt,
                Status = (int)a.Status,
                ImageUrls = a.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList()
            })
            .AsNoTracking()
            .ToListAsync();

        return Ok(ads);
    }

    /// <summary>
    /// Получение одного объявления (для страницы объявления)
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetAdById(Guid id)
    {
        var ad = await _dbContext.UserAds
            .Include(a => a.Images)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id);

        if (ad == null) return NotFound();

        var result = new UserAdDto
        {
            Id = ad.Id,
            Title = ad.Title,
            Description = ad.Description,
            Price = ad.Price,
            Region = ad.Region,
            Category = ad.Category,
            Latitude = ad.Latitude,
            Longitude = ad.Longitude,
            CreatedAt = ad.CreatedAt,
            Status = (int)ad.Status,
            ImageUrls = ad.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList()
        };

        return Ok(result);
    }
}