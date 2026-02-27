using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace FedresursScraper.Services;

public interface ICdtTradeStatusScraper
{
    /// <summary>
    /// Парсит общую страницу торгов ЦДТ и возвращает статус торгов целиком (например, "Торги не состоялись").
    /// </summary>
    Task<string?> GetTradeStatusAsync(string tradeNumber, CancellationToken ct);
}

public class CdtTradeStatusScraper : ICdtTradeStatusScraper
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CdtTradeStatusScraper> _logger;

    public CdtTradeStatusScraper(HttpClient httpClient, ILogger<CdtTradeStatusScraper> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string?> GetTradeStatusAsync(string tradeNumber, CancellationToken ct)
    {
        var tradeId = Regex.Replace(tradeNumber, @"\D", "");
        var url = $"https://bankrot.cdtrf.ru/public/undef/card/trade.aspx?id={tradeId}";

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var htmlContent = await response.Content.ReadAsStringAsync(ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            // Ищем элемент по его точному ID, который использует платформа ЦДТ для статуса
            var statusNode = doc.DocumentNode.SelectSingleNode("//span[@id='ctl00_cph1_lTradeStatusID']");

            if (statusNode != null)
            {
                var rawText = HtmlEntity.DeEntitize(statusNode.InnerText);
                var status = Regex.Replace(rawText, @"\s+", " ").Trim();

                if (status.Contains("не состоял", StringComparison.OrdinalIgnoreCase))
                    return "Торги не состоялись";

                if (status.Contains("отменен", StringComparison.OrdinalIgnoreCase))
                    return "Торги отменены";

                return status;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при парсинге статуса ЦДТ для торгов {TradeNumber}", tradeNumber);
        }

        return null;
    }
}
