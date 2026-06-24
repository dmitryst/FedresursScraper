public class MetsEnrichmentOptions
{
    public bool IsEnabled { get; set; } = true;

    public int DelayWhenNoWorkMinutes { get; set; } = 5;

    /// <summary>
    /// Минимальная задержка перед первым обогащением (в часах).
    /// Фото на сайте площадки могут появиться спустя несколько часов после создания лота.
    /// </summary>
    public int MinHoursBeforeFirstEnrichment { get; set; } = 3;

    /// <summary>
    /// Паузы между повторными попытками (в часах) после неудачи.
    /// Индекс 0 — пауза перед 2-й попыткой, индекс 1 — перед 3-й и т.д.
    /// </summary>
    public int[] MissingImagesRetryDelayHours { get; set; } = [4, 8];

    /// <summary>
    /// Сколько раз подряд можно не найти фото, прежде чем пометить торги как обогащённые.
    /// 0 — автоматически: MissingImagesRetryDelayHours.Length + 1.
    /// </summary>
    public int MaxMissingImagesAttempts { get; set; } = 3;

    public int GetEffectiveMaxMissingImagesAttempts()
    {
        if (MaxMissingImagesAttempts > 0)
            return MaxMissingImagesAttempts;

        var delays = MissingImagesRetryDelayHours;
        return delays is { Length: > 0 } ? delays.Length + 1 : 3;
    }

    public int GetMinRetryDelayHours()
    {
        var delays = MissingImagesRetryDelayHours;
        if (delays is not { Length: > 0 })
            return 4;

        return delays.Min();
    }

    public int GetRetryDelayHours(int completedAttempts)
    {
        var delays = MissingImagesRetryDelayHours;
        if (delays is not { Length: > 0 })
            delays = [4, 8];

        var index = Math.Clamp(Math.Max(0, completedAttempts - 1), 0, delays.Length - 1);
        return delays[index];
    }
}
