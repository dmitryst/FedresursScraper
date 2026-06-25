using Lots.Data;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lots.Application.Services.DeepSeek;

public class DeepSeekBudgetGuard : IDeepSeekBudgetGuard
{
    private const int CircuitBreakerRowId = 1;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeepSeekBudgetGuard> _logger;
    private readonly DeepSeekBudgetOptions _options;

    public DeepSeekBudgetGuard(
        IServiceScopeFactory scopeFactory,
        ILogger<DeepSeekBudgetGuard> logger,
        IOptions<DeepSeekBudgetOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    public async Task AcquireRequestSlotAsync(string caller, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var circuit = await GetOrCreateCircuitBreakerAsync(db, cancellationToken);
        if (circuit.OpenUntil.HasValue && circuit.OpenUntil.Value > DateTime.UtcNow)
        {
            throw new CircuitBreakerOpenException(
                $"DeepSeek API заблокирован до {circuit.OpenUntil:u}. Причина: {circuit.Reason ?? "не указана"}");
        }

        var now = DateTime.UtcNow;
        var dailyKey = $"d:{now:yyyy-MM-dd}";
        var hourlyKey = $"h:{now:yyyy-MM-dd'T'HH}";

        var daily = await IncrementRequestCountAsync(db, dailyKey, cancellationToken);
        var hourly = await IncrementRequestCountAsync(db, hourlyKey, cancellationToken);

        if (daily.RequestCount > _options.DailyRequestLimit)
        {
            await OpenCircuitAsync(
                db,
                circuit,
                now.AddHours(_options.BudgetExceededCooldownHours),
                $"Суточный лимит запросов ({_options.DailyRequestLimit}) исчерпан. Caller: {caller}",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogCritical(
                "DeepSeek: суточный лимит запросов исчерпан ({Count}/{Limit}). Caller={Caller}",
                daily.RequestCount,
                _options.DailyRequestLimit,
                caller);

            throw new CircuitBreakerOpenException(
                $"Суточный лимит DeepSeek ({_options.DailyRequestLimit} запросов) исчерпан. Повторите позже.");
        }

        if (hourly.RequestCount > _options.HourlyRequestLimit)
        {
            await OpenCircuitAsync(
                db,
                circuit,
                now.AddHours(1),
                $"Часовой лимит запросов ({_options.HourlyRequestLimit}) исчерпан. Caller: {caller}",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogCritical(
                "DeepSeek: часовой лимит запросов исчерпан ({Count}/{Limit}). Caller={Caller}",
                hourly.RequestCount,
                _options.HourlyRequestLimit,
                caller);

            throw new CircuitBreakerOpenException(
                $"Часовой лимит DeepSeek ({_options.HourlyRequestLimit} запросов) исчерпан. Повторите через час.");
        }

        if (daily.TokenCount >= _options.DailyTokenLimit)
        {
            await OpenCircuitAsync(
                db,
                circuit,
                now.AddHours(_options.BudgetExceededCooldownHours),
                $"Суточный лимит токенов ({_options.DailyTokenLimit}) исчерпан. Caller: {caller}",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogCritical(
                "DeepSeek: суточный лимит токенов исчерпан ({Count}/{Limit}). Caller={Caller}",
                daily.TokenCount,
                _options.DailyTokenLimit,
                caller);

            throw new CircuitBreakerOpenException(
                $"Суточный лимит токенов DeepSeek ({_options.DailyTokenLimit:N0}) исчерпан. Повторите позже.");
        }

        WarnIfApproachingLimit("запросов (сутки)", daily.RequestCount, _options.DailyRequestLimit, caller);
        WarnIfApproachingLimit("запросов (час)", hourly.RequestCount, _options.HourlyRequestLimit, caller);
        WarnIfApproachingLimit("токенов (сутки)", daily.TokenCount, _options.DailyTokenLimit, caller);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordTokenUsageAsync(string caller, long tokens, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || tokens <= 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

        var now = DateTime.UtcNow;
        var dailyKey = $"d:{now:yyyy-MM-dd}";

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var daily = await db.DeepSeekBudgetStates
            .FirstOrDefaultAsync(s => s.PeriodKey == dailyKey, cancellationToken);

        if (daily == null)
        {
            daily = new DeepSeekBudgetState
            {
                PeriodKey = dailyKey,
                RequestCount = 0,
                TokenCount = tokens,
                UpdatedAt = now
            };
            db.DeepSeekBudgetStates.Add(daily);
        }
        else
        {
            daily.TokenCount += tokens;
            daily.UpdatedAt = now;
        }

        if (daily.TokenCount > _options.DailyTokenLimit)
        {
            var circuit = await GetOrCreateCircuitBreakerAsync(db, cancellationToken);
            await OpenCircuitAsync(
                db,
                circuit,
                now.AddHours(_options.BudgetExceededCooldownHours),
                $"Суточный лимит токенов ({_options.DailyTokenLimit}) превышен после запроса. Caller: {caller}",
                cancellationToken);

            _logger.LogCritical(
                "DeepSeek: суточный лимит токенов превышен ({Count}/{Limit}) после запроса. Caller={Caller}",
                daily.TokenCount,
                _options.DailyTokenLimit,
                caller);
        }
        else
        {
            _logger.LogInformation(
                "DeepSeek usage: +{Tokens} tokens from {Caller}. Daily total: {DailyTokens}/{Limit}",
                tokens,
                caller,
                daily.TokenCount,
                _options.DailyTokenLimit);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task TripOnPaymentFailureAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

        var circuit = await GetOrCreateCircuitBreakerAsync(db, cancellationToken);
        var openUntil = DateTime.UtcNow.AddHours(_options.PaymentFailureCooldownHours);

        await OpenCircuitAsync(db, circuit, openUntil, "Баланс DeepSeek исчерпан (HTTP 402)", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogCritical(
            "DeepSeek: баланс исчерпан (402). API заблокирован до {OpenUntil:u}",
            openUntil);
    }

    public async Task TripOnRateLimitAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

        var circuit = await GetOrCreateCircuitBreakerAsync(db, cancellationToken);
        var openUntil = DateTime.UtcNow.AddMinutes(1);

        await OpenCircuitAsync(db, circuit, openUntil, "Rate limit DeepSeek (HTTP 429)", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("DeepSeek: rate limit (429). API заблокирован до {OpenUntil:u}", openUntil);
    }

    public async Task<DeepSeekUsageStatus> GetUsageStatusAsync(
        int recentDays = 7,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var dailyKey = $"d:{now:yyyy-MM-dd}";
        var hourlyKey = $"h:{now:yyyy-MM-dd'T'HH}";

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

        var circuit = await db.DeepSeekCircuitBreakers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == CircuitBreakerRowId, cancellationToken);

        var dailyState = await db.DeepSeekBudgetStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.PeriodKey == dailyKey, cancellationToken);

        var hourlyState = await db.DeepSeekBudgetStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.PeriodKey == hourlyKey, cancellationToken);

        var historyDays = Math.Clamp(recentDays, 1, 31);
        var historyFrom = now.Date.AddDays(-(historyDays - 1));
        var historyFromKey = $"d:{historyFrom:yyyy-MM-dd}";

        var recentStates = await db.DeepSeekBudgetStates
            .AsNoTracking()
            .Where(s => s.PeriodKey.StartsWith("d:") && string.Compare(s.PeriodKey, historyFromKey) >= 0)
            .OrderByDescending(s => s.PeriodKey)
            .ToListAsync(cancellationToken);

        var dailyRequests = dailyState?.RequestCount ?? 0;
        var dailyTokens = dailyState?.TokenCount ?? 0;
        var hourlyRequests = hourlyState?.RequestCount ?? 0;

        var isCircuitOpen = circuit?.OpenUntil.HasValue == true && circuit.OpenUntil.Value > now;

        return new DeepSeekUsageStatus
        {
            Enabled = _options.Enabled,
            AsOfUtc = now,
            CircuitBreaker = new DeepSeekCircuitBreakerStatus
            {
                IsOpen = isCircuitOpen,
                OpenUntil = isCircuitOpen ? circuit!.OpenUntil : null,
                Reason = isCircuitOpen ? circuit!.Reason : null
            },
            Daily = new DeepSeekPeriodUsage
            {
                PeriodKey = dailyKey,
                RequestCount = dailyRequests,
                RequestLimit = _options.DailyRequestLimit,
                TokenCount = dailyTokens,
                TokenLimit = _options.DailyTokenLimit,
                RequestUsagePercent = ToUsagePercent(dailyRequests, _options.DailyRequestLimit),
                TokenUsagePercent = ToUsagePercent(dailyTokens, _options.DailyTokenLimit)
            },
            Hourly = new DeepSeekHourlyUsage
            {
                PeriodKey = hourlyKey,
                RequestCount = hourlyRequests,
                RequestLimit = _options.HourlyRequestLimit,
                RequestUsagePercent = ToUsagePercent(hourlyRequests, _options.HourlyRequestLimit)
            },
            RecentDays = recentStates
                .Select(s => new DeepSeekDailyHistoryItem
                {
                    Date = s.PeriodKey.Length > 2 ? s.PeriodKey[2..] : s.PeriodKey,
                    RequestCount = s.RequestCount,
                    TokenCount = s.TokenCount
                })
                .ToList()
        };
    }

    private static double ToUsagePercent(long current, long limit)
    {
        if (limit <= 0)
        {
            return 0;
        }

        return Math.Round((double)current / limit * 100, 1);
    }

    private static async Task<DeepSeekCircuitBreaker> GetOrCreateCircuitBreakerAsync(
        LotsDbContext db,
        CancellationToken cancellationToken)
    {
        var circuit = await db.DeepSeekCircuitBreakers
            .FirstOrDefaultAsync(c => c.Id == CircuitBreakerRowId, cancellationToken);

        if (circuit != null)
        {
            return circuit;
        }

        circuit = new DeepSeekCircuitBreaker { Id = CircuitBreakerRowId };
        db.DeepSeekCircuitBreakers.Add(circuit);
        await db.SaveChangesAsync(cancellationToken);
        return circuit;
    }

    private static async Task<DeepSeekBudgetState> IncrementRequestCountAsync(
        LotsDbContext db,
        string periodKey,
        CancellationToken cancellationToken)
    {
        var state = await db.DeepSeekBudgetStates
            .FirstOrDefaultAsync(s => s.PeriodKey == periodKey, cancellationToken);

        var now = DateTime.UtcNow;
        if (state == null)
        {
            state = new DeepSeekBudgetState
            {
                PeriodKey = periodKey,
                RequestCount = 1,
                TokenCount = 0,
                UpdatedAt = now
            };
            db.DeepSeekBudgetStates.Add(state);
        }
        else
        {
            state.RequestCount++;
            state.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return state;
    }

    private static Task OpenCircuitAsync(
        LotsDbContext db,
        DeepSeekCircuitBreaker circuit,
        DateTime openUntil,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!circuit.OpenUntil.HasValue || openUntil > circuit.OpenUntil.Value)
        {
            circuit.OpenUntil = openUntil;
            circuit.Reason = reason;
            circuit.UpdatedAt = DateTime.UtcNow;
        }

        return db.SaveChangesAsync(cancellationToken);
    }

    private void WarnIfApproachingLimit(string metric, long current, long limit, string caller)
    {
        if (limit <= 0)
        {
            return;
        }

        var ratio = (double)current / limit;
        if (ratio >= 0.8)
        {
            _logger.LogWarning(
                "DeepSeek: использовано {Percent:P0} суточного лимита {Metric} ({Current}/{Limit}). Caller={Caller}",
                ratio,
                metric,
                current,
                limit,
                caller);
        }
    }
}
