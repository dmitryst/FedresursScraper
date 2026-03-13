using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Lots.Data;
using Lots.Data.Entities;
using System.Text;
using FedresursScraper.Services.Email;
using FedresursScraper.Services.Utils;

namespace FedresursScraper.Services.LotAlerts;

/// <summary>
/// Фоновый процесс, который собирает неотправленные уведомления (LotAlertMatches),
/// группирует их по пользователям и отправляет дайджесты в заданное пользователем время.
/// </summary>
public class LotAlertDeliveryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LotAlertDeliveryWorker> _logger;

    public LotAlertDeliveryWorker(IServiceProvider serviceProvider, ILogger<LotAlertDeliveryWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LotAlertDeliveryWorker запущен. Рассылка по расписанию (раз в час).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAlertDeliveriesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Критическая ошибка в цикле LotAlertDeliveryWorker.");
            }

            // РАСЧЕТ ВРЕМЕНИ СНА ДО СЛЕДУЮЩЕГО ЧАСА
            var now = DateTime.UtcNow;
            // Переходим на начало следующего часа (минуты и секунды в ноль)
            var nextHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);
            // Добавляем 1 минуту для страховки от преждевременного пробуждения
            nextHour = nextHour.AddMinutes(1);

            var timeToSleep = nextHour - now;

            _logger.LogInformation("Воркер засыпает на {Minutes:F1} минут до {NextRun:HH:mm} UTC.",
                timeToSleep.TotalMinutes, nextHour);

            await Task.Delay(timeToSleep, stoppingToken);
        }
    }

    private async Task ProcessAlertDeliveriesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var now = DateTime.UtcNow;

        // Определяем текущий час по МСК
        //var mskTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time"); 
        // Если хостимся в Linux/Docker, используем строку ниже, иначе строку выше
        var mskTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");

        var currentMskTime = TimeZoneInfo.ConvertTimeFromUtc(now, mskTimeZone);

        // Форматируем текущий час (например, "09:00" или "15:00")
        string currentMskHourStr = $"{currentMskTime.Hour:D2}:00";

        _logger.LogInformation("Проверка рассылки алертов на {CurrentHour} (МСК).", currentMskHourStr);

        // Отсекаем слишком старые матчи. Если матч валялся в БД больше 48 часов 
        // (например, сервис писем лежал или подписка была приостановлена и затем снова включена), 
        // мы не будем спамить ими юзера.
        var staleThreshold = now.AddHours(-48);

        // Берем все неотправленные совпадения, , включая данные о лоте, алерте и пользователе, 
        // но только для тех алертов, у которых DeliveryTimeStr совпадает с текущим часом по Москве
        // Благодаря частичному индексу (IX_LotAlertMatches_Unsent) это работает мгновенно.
        var unsentMatches = await dbContext.LotAlertMatches
            .Include(m => m.LotAlert)
                .ThenInclude(a => a.User)
            .Include(m => m.Lot)
            .Where(m => !m.IsSent
                     && m.CreatedAt > staleThreshold
                     && (m.LotAlert.DeliveryTimeStr == currentMskHourStr))
            .ToListAsync(stoppingToken);

        if (!unsentMatches.Any())
        {
            _logger.LogInformation("Нет неотправленных уведомлений на {CurrentHour}.", currentMskHourStr);
            return;
        }

        // Группируем совпадения по пользователю.
        var userGroups = unsentMatches.GroupBy(m => m.LotAlert.UserId).ToList();

        foreach (var group in userGroups)
        {
            var user = group.First().LotAlert.User;
            var matchesForUser = group.ToList();

            // Проверяем, что у пользователя все еще активен Pro-доступ 
            // (на случай, если подписка истекла между фазой матчинга и фазой отправки)
            if (!user.IsSubscriptionActive || (user.SubscriptionEndDate.HasValue && user.SubscriptionEndDate < now))
            {
                _logger.LogInformation("Пропуск отправки для пользователя {UserId}: нет Pro-доступа.", user.Id);

                // Помечаем как отправленные (отмененные), чтобы не пытаться снова
                MarkAsSent(matchesForUser, now);
                continue;
            }

            // Формируем тело письма (дайджест)
            var emailSubject = $"Найдены новые лоты по вашим подпискам ({matchesForUser.Count} шт.)";
            var emailBody = BuildEmailBody(user, matchesForUser);

            try
            {
                // Отправка
                await emailSender.SendEmailAsync(user.Email, emailSubject, emailBody);

                _logger.LogInformation("Письмо успешно отправлено пользователю {Email}. Найдено лотов: {Count}.",
                    user.Email, matchesForUser.Count);

                // Помечаем как успешно отправленные
                MarkAsSent(matchesForUser, now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка отправки письма пользователю {Email}", user.Email);
                // Если письмо не ушло, мы не ставим IsSent = true. 
                // Воркер попробует отправить их снова на следующей итерации.
            }
        }

        // Сохраняем статусы отправки в БД
        await dbContext.SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    /// Вспомогательный метод для пометки совпадений как отправленных.
    /// </summary>
    private static void MarkAsSent(List<LotAlertMatch> matches, DateTime sentTime)
    {
        foreach (var match in matches)
        {
            match.IsSent = true;
            match.SentAt = sentTime;
        }
    }

    /// <summary>
    /// Формирует HTML или текстовое содержимое письма со списком лотов.
    /// </summary>
    private static string BuildEmailBody(User user, List<LotAlertMatch> matches)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Здравствуйте!");
        sb.AppendLine($"По вашим сохраненным поискам найдены новые лоты:\n");

        // Группируем лоты внутри письма по ID самого алерта (поиска)
        var groupedByAlert = matches.GroupBy(m => m.LotAlertId);

        foreach (var alertGroup in groupedByAlert)
        {
            var alert = alertGroup.First().LotAlert;

            // Форматируем категории
            var categoriesText = alert.Categories != null && alert.Categories.Any()
                ? string.Join(", ", alert.Categories)
                : "Любые";

            // Форматируем регионы с использованием RegionCodeHelper
            var regionsText = "Любые регионы";
            if (alert.RegionCodes != null && alert.RegionCodes.Any())
            {
                var regionNames = new List<string>();
                foreach (var code in alert.RegionCodes)
                {
                    // Пытаемся получить красивое название региона по коду
                    var regionInfo = RegionCodeHelper.GetRegionByCode(code);
                    if (regionInfo.HasValue)
                    {
                        regionNames.Add(regionInfo.Value.RegionName);
                    }
                    else
                    {
                        // Если код почему-то не найден в словаре, выводим хотя бы код
                        regionNames.Add($"Код {code}");
                    }
                }
                regionsText = string.Join(", ", regionNames);
            }

            // Форматируем цену
            var priceText = "Любая";
            if (alert.MinPrice.HasValue || alert.MaxPrice.HasValue)
            {
                var minStr = alert.MinPrice.HasValue ? $"от {alert.MinPrice:N0}" : "";
                var maxStr = alert.MaxPrice.HasValue ? $"до {alert.MaxPrice:N0}" : "";
                priceText = $"{minStr} {maxStr}".Trim();
            }

            // Форматируем вид торгов
            var biddingTypeText = string.IsNullOrEmpty(alert.BiddingType)
                ? "Все"
                : alert.BiddingType;

            // Форматируем собственность
            var ownershipText = !alert.IsSharedOwnership.HasValue
                ? "Все"
                : alert.IsSharedOwnership.Value ? "Только доли" : "Целиком";

            // Выводим заголовок подписки
            sb.AppendLine($"=== Подписка ===");
            sb.AppendLine($"Категории: {categoriesText}");
            sb.AppendLine($"Регионы: {regionsText}");
            sb.AppendLine($"Цена (руб.): {priceText}");
            sb.AppendLine($"Вид торгов: {biddingTypeText}");
            sb.AppendLine($"Собственность: {ownershipText}");
            sb.AppendLine("------------------");

            // Выводим лоты, найденные по этой подписке
            
            // Сортируем матчи от новых к старым
            var sortedMatches = alertGroup.OrderByDescending(m => m.Lot.CreatedAt).ToList();
            
            // ОГРАНИЧЕНИЕ: Берем максимум 50 лотов на один алерт!
            var topMatches = sortedMatches.Take(50).ToList();

            foreach (var match in topMatches)
            {
                var lot = match.Lot;
                var title = string.IsNullOrEmpty(lot.Title) ? "Лот без названия" : lot.Title;
                var lotPriceText = lot.StartPrice.HasValue ? $"{lot.StartPrice:N2} руб." : "Цена не указана";

                sb.AppendLine($"- {title}");
                sb.AppendLine($"  Начальная цена: {lotPriceText}");
                sb.AppendLine($"  Ссылка: https://s-lot.ru/lot/{lot.PublicId}");
                sb.AppendLine();
            }

            // Если лотов было больше 50, сообщаем об этом пользователю
            if (sortedMatches.Count > 50)
            {
                var hiddenCount = sortedMatches.Count - 50;
                sb.AppendLine($"... и еще {hiddenCount} лотов.");
                sb.AppendLine($"Зайдите на сайт s-lot.ru, чтобы посмотреть их все.");
                sb.AppendLine();
            }
        }

        sb.AppendLine("С уважением,\nКоманда сервиса поиска лотов s-lot.ru.");
        return sb.ToString();
    }
}
