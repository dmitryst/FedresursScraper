namespace FedresursScraper.Integrations.Fedresurs.Utils;

public static class BiddingTypeMapper
{
    public static string GetRussianName(string? apiType)
    {
        if (string.IsNullOrWhiteSpace(apiType)) return "Неизвестно";

        return apiType switch
        {
            "OpenedAuction" => "Открытый аукцион",
            "PublicOffer" => "Публичное предложение",
            "OpenedConcours" => "Открытый конкурс",
            "ClosedAuction" => "Закрытый аукцион",
            "ClosedConcours" => "Закрытый конкурс",
            _ => apiType // Fallback: если пришло что-то новое, запишем как есть, чтобы не потерять
        };
    }
}