// Файл: /Controllers/HealthController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace FedresursScraper.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public HealthController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Возвращает текущую версию бэкенд-приложения.
    /// </summary>
    [HttpGet("version")]
    [AllowAnonymous]
    public IActionResult GetVersion()
    {
        var version = _configuration["AppInfo:Version"] ?? "unknown";
        return Ok(new { Version = version });
    }
}
