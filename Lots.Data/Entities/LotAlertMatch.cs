namespace Lots.Data.Entities;

/// <summary>
/// Очередь (Outbox) для найденных лотов, которые нужно отправить пользователю.
/// </summary>
public class LotAlertMatch
{
    public Guid Id { get; set; }
    
    public Guid LotAlertId { get; set; }
    public LotAlert LotAlert { get; set; } = null!;
    
    public Guid LotId { get; set; }
    public Lot Lot { get; set; } = null!;
    
    /// <summary>
    /// Статус отправки (доставлено ли уведомление)
    /// </summary>
    public bool IsSent { get; set; } = false;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
}
