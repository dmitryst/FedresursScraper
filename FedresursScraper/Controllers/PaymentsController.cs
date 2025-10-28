// Файл: /Controllers/PaymentsController.cs

using Microsoft.AspNetCore.Mvc;
using Yandex.Checkout.V3;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace FedresursScraper.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly LotsDbContext _dbContext;
    private readonly Client _yooKassaClient;
    private readonly AsyncClient _asyncClient;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        LotsDbContext dbContext,
        IConfiguration configuration,
        ILogger<PaymentsController> logger)
    {
        _dbContext = dbContext;
        _yooKassaClient = new Client(
            shopId: configuration["YooKassa:ShopId"],
            secretKey: configuration["YooKassa:SecretKey"]
        );
        _asyncClient = _yooKassaClient.MakeAsync();
        _logger = logger;
    }

    /// <summary>
    /// Создает платежную сессию на основе выбранного тарифного плана.
    /// </summary>
    [HttpPost("create-session")]
    [Authorize]
    public async Task<IActionResult> CreatePaymentSession([FromBody] CreateSessionRequest request)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString))
        {
            return Unauthorized("Не удалось определить пользователя.");
        }

        // --- логика выбора тарифа ---
        decimal amount;
        string description;

        switch (request.PlanId)
        {
            case "pro-month":
                amount = 500.00M;
                description = "Подписка на 1 месяц на s-lot.ru";
                break;
            case "pro-year":
                amount = 5000.00M;
                description = "Подписка на 1 год на s-lot.ru";
                break;
            default:
                _logger.LogWarning("Получен неизвестный planId: {PlanId}", request.PlanId);
                return BadRequest("Неизвестный тарифный план.");
        }

        var newPayment = new NewPayment
        {
            Amount = new Amount { Value = amount, Currency = "RUB" },
            Description = description,
            Confirmation = new Confirmation
            {
                Type = ConfirmationType.Redirect,
                ReturnUrl = "https://www.s-lot.ru/payment-success"
            },
            Capture = true,
            Metadata = new Dictionary<string, string>
            {
                { "userId", userIdString },
                { "planId", request.PlanId }
            }
        };

        try
        {
            // Создаем платеж асинхронно
            var createdPayment = await _asyncClient.CreatePaymentAsync(newPayment);

            // Возвращаем ссылку на оплату
            return Ok(new { ConfirmationUrl = createdPayment.Confirmation.ConfirmationUrl });
        }
        catch (YandexCheckoutException ex)
        {
            _logger.LogError(ex, "Ошибка при создании платежа для пользователя {UserId}", userIdString);
            return BadRequest(new { message = "Ошибка создания платежа", details = ex.Message });
        }
    }

    /// <summary>
    /// Принимает и обрабатывает webhook-уведомления от ЮKassa.
    /// </summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> YooKassaWebhook()
    {
        // Читаем тело запроса в строку
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync();
        
        Notification notification;
        try
        {
            notification = Client.ParseMessage(Request.Method, Request.ContentType, json);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Ошибка парсинга webhook от ЮKassa. JSON: {WebhookJson}", json);
            return BadRequest("Некорректный json");
        }

        if (notification is PaymentSucceededNotification succeededNotification)
        {
            var payment = succeededNotification.Object;

            if (payment.Paid &&
                payment.Metadata.TryGetValue("userId", out var userIdString) &&
                Guid.TryParse(userIdString, out Guid userGuid))
            {
                var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userGuid);
                if (user != null)
                {
                    user.IsSubscriptionActive = true;

                    // --- логика продления подписки ---
                    if (payment.Metadata.TryGetValue("planId", out var planId))
                    {
                        // Определяем, от какой даты считать продление
                        var startDate = (user.SubscriptionEndDate.HasValue && user.SubscriptionEndDate.Value > DateTime.UtcNow)
                            ? user.SubscriptionEndDate.Value
                            : DateTime.UtcNow;

                        switch (planId)
                        {
                            case "pro-month":
                                user.SubscriptionEndDate = startDate.AddMonths(1);
                                break;
                            case "pro-year":
                                user.SubscriptionEndDate = startDate.AddYears(1);
                                break;
                            default:
                                _logger.LogWarning("Неизвестный planId '{PlanId}' в webhook для пользователя {UserId}", planId, user.Id);
                                break;
                        }

                        _logger.LogInformation(
                            "Подписка для пользователя {Email} продлена по тарифу '{PlanId}'. Новая дата окончания: {SubscriptionEndDate}",
                            user.Email,
                            planId,
                            user.SubscriptionEndDate);
                    }
                    else
                    {
                        _logger.LogWarning("В метаданных платежа {PaymentId} отсутствует planId для пользователя {UserId}", payment.Id, user.Id);
                    }

                    await _dbContext.SaveChangesAsync();
                }
            }
        }
        
        return Ok();
    }
}
