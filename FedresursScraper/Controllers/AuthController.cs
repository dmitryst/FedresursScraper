using Lots.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

public record AuthDto(string Email, string Password);

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly LotsDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public AuthController(LotsDbContext context, IConfiguration configuration)
    {
        _dbContext = context;
        _configuration = configuration;
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMe()
    {
        // Берем ID пользователя из токена/куки
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdString, out Guid userId)) return Unauthorized();

        // Идем в базу за свежей информацией
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null) return Unauthorized();

        return Ok(new
        {
            id = user.Id,
            email = user.Email,
            isSubscriptionActive = user.HasProAccess,
            isOnTrial = user.IsOnTrial,
            subscriptionEndDate = user.SubscriptionEndDate,
            createdAt = user.CreatedAt
        });
    }


    [HttpPost("register")]
    public async Task<IActionResult> Register(AuthDto request)
    {
        if (await _dbContext.Users.AnyAsync(u => u.Email == request.Email))
        {
            return BadRequest("Пользователь с таким email уже существует.");
        }

        var newUser = new User
        {
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(newUser);
        await _dbContext.SaveChangesAsync();

        return Ok("Регистрация прошла успешно.");
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(AuthDto request)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized("Неверный email или пароль.");
        }

        var token = GenerateJwtToken(user!);

        // Установка токена в httpOnly cookie
        Response.Cookies.Append("access_token", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Expires = DateTime.UtcNow.AddHours(1),
            // для отладки
            //Secure = false, // В продакшене должно быть true
            //SameSite = SameSiteMode.Strict, // или Lax
        });

        return Ok(new { message = "Вход выполнен успешно.", email = user.Email });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        // Удаляем куку, устанавливая ей истекший срок
        Response.Cookies.Append("access_token", "", new CookieOptions
        {
            HttpOnly = true,
            Secure = true, // Важно: должно совпадать с тем, как ставили (true для https/prod)
            SameSite = SameSiteMode.None, // Также должно совпадать
            Expires = DateTime.UtcNow.AddDays(-1) // Прошедшая дата
        });

        return Ok(new { message = "Logged out" });
    }

    private string GenerateJwtToken(User user)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
