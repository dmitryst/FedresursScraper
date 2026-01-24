namespace FedresursScraper.Controllers.Models;

public class LotDocumentDto
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Extension { get; set; }
}
