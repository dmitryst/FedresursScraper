namespace Lots.Data.Entities;

/// <summary>
/// Таблица-буфер, которая будет хранить только даты обновления следующей проверки.
/// </summary>
public class BiddingScheduleUpdate
{
    public Guid Id { get; set; }
    public Guid BiddingId { get; set; }
    public DateTime NextStatusCheckAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsExported { get; set; } = false;
}