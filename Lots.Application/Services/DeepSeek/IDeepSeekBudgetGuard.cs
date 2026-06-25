namespace Lots.Application.Services.DeepSeek;

public interface IDeepSeekBudgetGuard
{
    /// <summary>
    /// Проверяет лимиты и резервирует один слот запроса.
    /// Бросает <see cref="CircuitBreakerOpenException"/>, если лимит исчерпан.
    /// </summary>
    Task AcquireRequestSlotAsync(string caller, CancellationToken cancellationToken = default);

    /// <summary>Учитывает фактическое потребление токенов после успешного ответа API.</summary>
    Task RecordTokenUsageAsync(string caller, long tokens, CancellationToken cancellationToken = default);

    /// <summary>Открывает circuit breaker после HTTP 402 (баланс исчерпан).</summary>
    Task TripOnPaymentFailureAsync(CancellationToken cancellationToken = default);

    /// <summary>Открывает circuit breaker после HTTP 429.</summary>
    Task TripOnRateLimitAsync(CancellationToken cancellationToken = default);

    /// <summary>Текущий расход и состояние предохранителя (только чтение).</summary>
    Task<DeepSeekUsageStatus> GetUsageStatusAsync(int recentDays = 7, CancellationToken cancellationToken = default);
}
