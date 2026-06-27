namespace FedresursScraper.Controllers.Models;

public class LotDocumentDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Extension { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public bool IsExternal { get; set; }
}
