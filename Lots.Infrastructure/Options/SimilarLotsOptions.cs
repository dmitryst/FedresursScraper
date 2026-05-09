namespace FedresursScraper.Options;

public class SimilarLotsOptions
{
    public bool Enabled { get; set; } = true;
    public int RunAtHour { get; set; } = 2; // Default to 2 AM
    public int BatchSize { get; set; } = 100;
}
