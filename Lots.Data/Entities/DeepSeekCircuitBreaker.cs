namespace Lots.Data.Entities;

/// <summary>
/// Глобальное состояние circuit breaker для DeepSeek API (одна строка, Id = 1).
/// </summary>
public class DeepSeekCircuitBreaker
{
    public int Id { get; set; } = 1;

    public DateTime? OpenUntil { get; set; }

    public string? Reason { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
