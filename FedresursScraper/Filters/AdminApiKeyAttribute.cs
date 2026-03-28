using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FedresursScraper.Controllers;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AdminApiKeyAttribute : Attribute, IAsyncActionFilter
{
    // Имя заголовка, который мы будем проверять
    private const string ApiKeyHeaderName = "X-Admin-Api-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Получаем эталонный ключ
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expectedApiKey = configuration.GetValue<string>("AdminSettings:ApiKey");

        // Защита от дурака: если ключ на сервере забыли настроить, никого не пускаем
        if (string.IsNullOrEmpty(expectedApiKey))
        {
            context.Result = new UnauthorizedObjectResult("Admin API Key is not configured on the server.");
            return;
        }

        // Проверяем, прислал ли клиент заголовок
        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey))
        {
            context.Result = new UnauthorizedObjectResult("API Key is missing in headers.");
            return;
        }

        // Сравниваем ключи
        if (!expectedApiKey.Equals(extractedApiKey))
        {
            context.Result = new UnauthorizedObjectResult("Invalid API Key.");
            return;
        }

        // Если всё совпало, передаем управление контроллеру
        await next();
    }
}