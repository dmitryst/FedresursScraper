namespace FedresursScraper.Integrations.Fedresurs.Models;

public class FedresursApiOptions
{
    public string BaseUrl { get; set; } = "https://bank-publications-demo.fedresurs.ru/"; // Или prod
    public string Login { get; set; } = null!;
    public string Password { get; set; } = null!;
}
