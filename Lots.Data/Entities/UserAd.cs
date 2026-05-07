namespace Lots.Data.Entities;

public class UserAd
{
    public Guid Id { get; set; }
    
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    public string Title { get; set; } = default!;
    public string Description { get; set; } = default!;
    public decimal Price { get; set; }

    // Дата создания и обновления
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Статус объявления (Черновик, Активно, На модерации, Закрыто)
    public AdStatus Status { get; set; }

    // География (если захотите выводить их на ту же карту, просто добавьте координаты)
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Фотографии
    public ICollection<UserAdImage> Images { get; set; } = new List<UserAdImage>();
}

public enum AdStatus
{
    Draft = 0,
    Active = 1,
    UnderModeration = 2,
    Closed = 3
}