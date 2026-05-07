using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace FedresursScraper.UserAds;

public class CreateUserAdDto
{
    [Required(ErrorMessage = "Укажите название")]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    [Required(ErrorMessage = "Укажите описание")]
    [MaxLength(2000)]
    public string Description { get; set; } = null!;

    [Range(1, double.MaxValue, ErrorMessage = "Цена должна быть больше нуля")]
    public decimal Price { get; set; }

    public string? Latitude { get; set; }
    public string? Longitude { get; set; }

    // Список загружаемых файлов
    [MaxFileSize(5 * 1024 * 1024)]
    public List<IFormFile>? Images { get; set; }
}