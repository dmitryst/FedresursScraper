namespace Lots.Data.Entities;

public class UserAdImage
{
    public Guid Id { get; set; }
    public Guid UserAdId { get; set; }
    public UserAd UserAd { get; set; } = default!;

    public string Url { get; set; } = default!;

    // Флаг для главного фото
    public bool IsMain { get; set; }

    // Порядок отображения
    public int Order { get; set; }
}