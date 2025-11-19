// FedresursScraper.Services/LotDetailParser.cs

using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using FedresursScraper.Services.Models;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using System.Globalization;

namespace FedresursScraper.Services;

public class LotDetailParser : ILotDetailParser
{
    private readonly ILogger<LotDetailParser> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HtmlParser _htmlParser;

    public LotDetailParser(ILogger<LotDetailParser> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _htmlParser = new HtmlParser();
    }

    public async Task<LotDetails> ParseDetailsAsync(Guid lotId, CancellationToken cancellationToken = default)
    {
        var url = $"https://fedresurs.ru/biddings/{lotId}";
        var client = _httpClientFactory.CreateClient("FedresursScraper");

        try
        {
            var htmlContent = await client.GetStringAsync(url, cancellationToken);
            var document = await _htmlParser.ParseDocumentAsync(htmlContent, cancellationToken);

            var details = new LotDetails
            {
                BiddingType = ParseField(document, "Вид торгов"),
                BidAcceptancePeriod = ParseField(document, "Прием заявок"),
                //ViewingProcedure = ParseField(document, "Порядок ознакомления с имуществом"),
                Description = ParseDescription(document),
                StartPrice = ParsePrice(ParseField(document, "Начальная цена")),
                Step = ParsePrice(ParseField(document, "Шаг")),
                Deposit = ParsePrice(ParseField(document, "Задаток")),
                //AnnouncedAt = ParseBiddingAnnouncementDate(ParseField(document, "Дата и время начала представления заявок")),
                BankruptMessageId = ParseBankruptMessageId(document),
                CadastralNumbers = ParseCadastralNumbers(document),
            };
            
            return details;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось распарсить страницу деталей лота: {Url}", url);
            throw;
        }
    }

    private string? ParseField(IDocument document, string fieldName)
    {
        var element = document.QuerySelectorAll(".data-row")
            .FirstOrDefault(e => e.QuerySelector(".data-label")?.TextContent.Trim() == fieldName);

        return element?.QuerySelector(".data-value")?.TextContent.Trim();
    }
    
    private string? ParseLotCategory(IDocument document) => ParseField(document, "Категория имущества");

    private string? ParseDescription(IDocument document) => ParseField(document, "Описание");

    private List<string>? ParseCadastralNumbers(IDocument document)
    {
        var rawText = ParseField(document, "Кадастровый номер");
        if (string.IsNullOrWhiteSpace(rawText)) return null;

        return Regex.Matches(rawText, @"\d{2}:\d{2}:\d{6,7}:\d{1,4}")
            .Select(m => m.Value)
            .ToList();
    }
    
    private (double?, double?) ParseLotCoords(IDocument document)
    {
        var rawCoords = ParseField(document, "Координаты");
        if (string.IsNullOrWhiteSpace(rawCoords)) return (null, null);

        var matches = Regex.Matches(rawCoords, @"[0-9]+\.[0-9]+");
        if (matches.Count != 2) return (null, null);
        
        var culture = CultureInfo.InvariantCulture;
        double.TryParse(matches[0].Value, culture, out var lat);
        double.TryParse(matches[1].Value, culture, out var lon);

        return (lat, lon);
    }

    private decimal? ParsePrice(string? priceText)
    {
        if (string.IsNullOrWhiteSpace(priceText)) return null;
        var cleaned = Regex.Replace(priceText, @"[^\d,]", "").Replace(',', '.');
        if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
        {
            return price;
        }
        return null;
    }

    private DateTime? ParseBiddingAnnouncementDate(string? rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate)) return null;
        if (DateTime.TryParse(rawDate, null, DateTimeStyles.AssumeLocal, out var date))
        {
            return date;
        }
        return null;
    }

    private Guid ParseBankruptMessageId(IDocument document)
    {
        var link = document.QuerySelector("a[href*='bankruptmessage']");
        var href = link?.GetAttribute("href");
        if (href == null) return Guid.Empty;

        var match = Regex.Match(href, @"\?id=([A-F0-9]{32})", RegexOptions.IgnoreCase);
        if (match.Success && Guid.TryParse(match.Groups[1].Value, out var guid))
        {
            return guid;
        }
        return Guid.Empty;
    }
}
