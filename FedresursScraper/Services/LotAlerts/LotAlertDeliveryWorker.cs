using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Lots.Data;
using Lots.Data.Entities;
using System.Text;
using FedresursScraper.Services.Email;

namespace FedresursScraper.Services.LotAlerts;

/// <summary>
/// Фоновый процесс, который собирает неотправленные уведомления (LotAlertMatches),
/// группирует их по пользователям и отправляет дайджесты (Email/Telegram).
/// </summary>
public class LotAlertDeliveryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LotAlertDeliveryWorker> _logger;
    
    // Как часто отправлять рассылку (например, раз в час)
    private readonly TimeSpan _deliveryInterval = TimeSpan.FromHours(1);

    public LotAlertDeliveryWorker(IServiceProvider serviceProvider, ILogger<LotAlertDeliveryWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LotAlertDeliveryWorker запущен. Интервал рассылки: {Interval}", _deliveryInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();
                
                var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

                var now = DateTime.UtcNow;

                // Берем все неотправленные совпадения, включая данные о лоте, алерте и пользователе.
                // Благодаря частичному индексу (IX_LotAlertMatches_Unsent) это работает мгновенно.
                var unsentMatches = await dbContext.LotAlertMatches
                    .Include(m => m.LotAlert)
                        .ThenInclude(a => a.User)
                    .Include(m => m.Lot)
                    .Where(m => !m.IsSent)
                    // Берем с запасом, но ограничиваем, чтобы не выедать память, если лотов слишком много
                    .Take(1000) 
                    .ToListAsync(stoppingToken);

                if (unsentMatches.Any())
                {
                    // Группируем совпадения по пользователю.
                    // Ключ - UserId. Значение - список матчей для этого пользователя.
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
                            
                            _logger.LogInformation("Письмо успешно 'отправлено' пользователю {Email}. Найдено лотов: {Count}.", 
                                user.Email, matchesForUser.Count);

                            // Помечаем как успешно отправленные
                            MarkAsSent(matchesForUser, now);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Ошибка отправки письма пользователю {Email}", user.Email);
                            // Если письмо не ушло, мы гн ставим IsSent = true. 
                            // Воркер попробует отправить их снова на следующей итерации.
                        }
                    }

                    // Сохраняем статусы отправки в БД
                    await dbContext.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Критическая ошибка в цикле LotAlertDeliveryWorker.");
            }

            // Ждем до следующего окна рассылки
            await Task.Delay(_deliveryInterval, stoppingToken);
        }
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
            var categoriesText = alert.Categories != null ? string.Join(", ", alert.Categories) : "Любые";
            var regionsText = alert.RegionCodes != null ? string.Join(", ", alert.RegionCodes) : "Любые регионы";
            
            sb.AppendLine($"=== Подписка: Категории: {categoriesText} | Регионы: {regionsText} ===");

            foreach (var match in alertGroup)
            {
                var lot = match.Lot;
                var title = string.IsNullOrEmpty(lot.Title) ? "Лот без названия" : lot.Title;
                var priceText = lot.StartPrice.HasValue ? $"{lot.StartPrice:N2} руб." : "Цена не указана";
                
                sb.AppendLine($"- {title}");
                sb.AppendLine($"  Начальная цена: {priceText}");
                sb.AppendLine($"  Ссылка: https://s-lot.ru/lot/{lot.PublicId}"); // Пример ссылки
                sb.AppendLine();
            }
        }

        sb.AppendLine("С уважением,\nКоманда сервиса поиска лотов s-lot.ru.");
        return sb.ToString();
    }
}
