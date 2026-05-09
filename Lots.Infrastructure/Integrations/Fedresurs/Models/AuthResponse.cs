using System.Text.Json.Serialization;

namespace FedresursScraper.Integrations.Fedresurs.Models;

/// <summary>
/// Объект с возвращаемыми данными при успешной авторизации.
/// </summary>
public class AuthResponse
{
    /// <summary>
    /// JWT-токен.
    /// </summary>
    [JsonPropertyName("jwt")]
    public string Jwt { get; set; } = null!;
}