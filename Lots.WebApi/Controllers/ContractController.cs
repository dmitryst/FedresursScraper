using Lots.Application.DTOs;
using Lots.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Lots.WebApi.Controllers;

[ApiController]
[Route("api/contracts")]
public class ContractController : ControllerBase
{
    private readonly ContractGenerationService _contractService;
    private readonly IConfiguration _configuration;

    public ContractController(ContractGenerationService contractService, IConfiguration configuration)
    {
        _contractService = contractService;
        _configuration = configuration;
    }

    [HttpGet("permission/{lotId}")]
    [Authorize]
    public async Task<IActionResult> CheckPermission(Guid lotId)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
        {
            return Unauthorized();
        }

        var hasPermission = await _contractService.HasPermissionAsync(userId, lotId);
        return Ok(new { hasPermission });
    }

    [HttpPost("generate/{lotId}")]
    [Authorize]
    public async Task<IActionResult> GenerateContract(Guid lotId, [FromBody] ContractGenerationRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
        {
            return Unauthorized();
        }

        try
        {
            // Получаем путь к шаблону из папки Templates в выходном каталоге
            string templatePath = Path.Combine(AppContext.BaseDirectory, "Templates", "agentskiy-dogovor.docx");

            if (!System.IO.File.Exists(templatePath))
            {
                return StatusCode(500, "Файл шаблона договора не найден на сервере.");
            }

            var fileBytes = await _contractService.GenerateContractAsync(userId, lotId, request, templatePath);

            return File(fileBytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"Contract_{lotId}.docx");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Внутренняя ошибка сервера: {ex.Message}");
        }
    }
}