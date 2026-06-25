namespace Lots.Application.Services.DeepSeek;

public class DeepSeekUsageStatus
{
    public bool Enabled { get; set; }

    public DateTime AsOfUtc { get; set; }

    public DeepSeekCircuitBreakerStatus CircuitBreaker { get; set; } = new();

    public DeepSeekPeriodUsage Daily { get; set; } = new();

    public DeepSeekHourlyUsage Hourly { get; set; } = new();

    public IReadOnlyList<DeepSeekDailyHistoryItem> RecentDays { get; set; } = Array.Empty<DeepSeekDailyHistoryItem>();
}

public class DeepSeekCircuitBreakerStatus
{
    public bool IsOpen { get; set; }

    public DateTime? OpenUntil { get; set; }

    public string? Reason { get; set; }
}

public class DeepSeekPeriodUsage
{
    public string PeriodKey { get; set; } = string.Empty;

    public long RequestCount { get; set; }

    public int RequestLimit { get; set; }

    public long TokenCount { get; set; }

    public long TokenLimit { get; set; }

    public double RequestUsagePercent { get; set; }

    public double TokenUsagePercent { get; set; }
}

public class DeepSeekHourlyUsage
{
    public string PeriodKey { get; set; } = string.Empty;

    public long RequestCount { get; set; }

    public int RequestLimit { get; set; }

    public double RequestUsagePercent { get; set; }
}

public class DeepSeekDailyHistoryItem
{
    public string Date { get; set; } = string.Empty;

    public long RequestCount { get; set; }

    public long TokenCount { get; set; }
}
