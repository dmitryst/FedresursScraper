using System.Text.Json.Serialization;

namespace FedresursScraper.Services.Models;

public class FedresursAttachmentInfo
{
    public string Title { get; set; } = default!;
    public string Url { get; set; } = default!;
    public string Extension { get; set; } = default!;

    [JsonIgnore]
    public byte[]? Content { get; set; }
}

public class BankruptMessageScrapeResult
{
    public List<LotInfo> Lots { get; set; } = [];
    public List<FedresursAttachmentInfo> Attachments { get; set; } = [];
}
