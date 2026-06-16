namespace Lots.Application.Services.DebtScoring;

public class DebtScoringOptions
{
    public const string SectionName = "DebtScoring";

    public bool IsEnabled { get; set; }

    /// <summary>
    /// Минимальный номинал задолженности для обработки (руб.).
    /// </summary>
    public decimal MinDebtNominal { get; set; } = 100_000m;

    public int BatchSize { get; set; } = 5;

    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Пауза между пакетами обработки (секунды).
    /// </summary>
    public int DelayBetweenBatchesSeconds { get; set; } = 15;

    /// <summary>
    /// Пауза при отсутствии работы (минуты).
    /// </summary>
    public int DelayWhenNoWorkMinutes { get; set; } = 10;

    /// <summary>
    /// Задержка перед повторной попыткой после ошибки (минуты).
    /// </summary>
    public int RetryDelayMinutes { get; set; } = 30;

    /// <summary>
    /// URL Python-сервиса OCR (POST /ocr).
    /// </summary>
    public string? OcrServiceUrl { get; set; }

    public int EnrichmentMaxAttempts { get; set; } = 3;

    public int EnrichmentRetryDelayMinutes { get; set; } = 30;

    public bool EnableDadataStep { get; set; }

    public bool EnableBankruptcyStep { get; set; }

    public bool EnableKadStep { get; set; }

    public bool EnableFsspStep { get; set; }
}
