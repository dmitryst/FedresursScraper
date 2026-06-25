namespace Lots.Application.Services.DeepSeek;

public class DeepSeekBudgetOptions
{
    public const string SectionName = "DeepSeek:BudgetGuard";

    /// <summary>Включить защиту бюджета. По умолчанию true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Максимум API-запросов за сутки (UTC).</summary>
    public int DailyRequestLimit { get; set; } = 500;

    /// <summary>Максимум токенов за сутки (UTC).</summary>
    public long DailyTokenLimit { get; set; } = 20_000_000;

    /// <summary>Максимум API-запросов за час (UTC) — защита от внезапных всплесков.</summary>
    public int HourlyRequestLimit { get; set; } = 80;

    /// <summary>Пауза после исчерпания баланса (HTTP 402), в часах.</summary>
    public int PaymentFailureCooldownHours { get; set; } = 24;

    /// <summary>Пауза после превышения лимита бюджета, в часах (до конца суток + этот период не нужен — сброс в полночь UTC).</summary>
    public int BudgetExceededCooldownHours { get; set; } = 6;
}
