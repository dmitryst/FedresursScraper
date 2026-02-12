namespace Lots.Data.Entities;

/// <summary>
/// Статистика запуска пользователями детального анализа лотов
/// </summary>
public class LotEvaluationUserRunStatistics
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public Guid LotId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
