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
    private readonly LotsDbContext _context;
    private readonly IConfiguration _configuration;

    public AuthController(LotsDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult GetMe()
    {
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        return Ok(new { email });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(AuthDto request)
    {
        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
        {
            return BadRequest("Пользователь с таким email уже существует.");
        }

        var newUser = new User
        {
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();

        return Ok("Регистрация прошла успешно.");
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(AuthDto request)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);

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
