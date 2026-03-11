using System.ComponentModel.DataAnnotations;

namespace FedresursScraper.Models.LotAlerts;

// Используется при создании или обновлении (пользователь присылает эти данные)
public class UpsertLotAlertRequest
{
    public string[]? RegionCodes { get; set; }
    
    // Валидация: хотя бы один регион или категория должны быть указаны
    public string[]? Categories { get; set; }
    
    [Range(0, double.MaxValue, ErrorMessage = "Минимальная цена не может быть отрицательной")]
    public decimal? MinPrice { get; set; }
    
    public decimal? MaxPrice { get; set; }
    
    public bool IsActive { get; set; } = true;
}
