namespace Lots.Data.Entities;

/// <summary>
/// Сохраненный поиск пользователя (настройка оповещений).
/// </summary>
public class LotAlert
{
    public Guid Id { get; set; }
    
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    // --- Параметры фильтрации ---
    // В PostgreSQL EF Core автоматически замаппит string[] в тип text[]
    public string[]? RegionCodes { get; set; } 
    public string[]? Categories { get; set; }
    
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    
    /// <summary>
    /// Включено ли оповещение пользователем
    /// </summary>
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
