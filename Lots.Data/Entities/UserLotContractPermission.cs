using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities;

/// <summary>
/// Разрешение для пользователя на формирование агентского договора по конкретному лоту
/// </summary>
public class UserLotContractPermission
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    [ForeignKey("UserId")]
    public User User { get; set; } = default!;

    public Guid LotId { get; set; }
    [ForeignKey("LotId")]
    public Lot Lot { get; set; } = default!;

    /// <summary>
    /// Фиксированная часть вознаграждения за подготовку и подачу заявки (руб.)
    /// </summary>
    public decimal FixedRewardAmount { get; set; }

    /// <summary>
    /// Бонусная часть вознаграждения агента в случае победы в торгах (руб.)
    /// </summary>
    public decimal SuccessRewardAmount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
