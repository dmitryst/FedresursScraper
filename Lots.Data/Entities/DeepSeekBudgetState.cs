using System.ComponentModel.DataAnnotations;

namespace Lots.Data.Entities;

/// <summary>
/// Счётчики использования DeepSeek API за период (день или час, UTC).
/// Ключ: "d:2026-06-25" или "h:2026-06-25T14".
/// </summary>
public class DeepSeekBudgetState
{
    [Key]
    [MaxLength(20)]
    public string PeriodKey { get; set; } = string.Empty;

    public long RequestCount { get; set; }

    public long TokenCount { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
