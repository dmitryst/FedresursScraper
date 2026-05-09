using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lots.Data.Entities;
using Lots.Data;
using FedresursScraper.Integrations.Fedresurs.Utils;
using FedresursScraper.Integrations.Fedresurs.Models;
using Microsoft.Extensions.Options;

namespace FedresursScraper.Integrations.Fedresurs.Processors;

public class FedresursMessageProcessorService : BackgroundService
{
    private readonly ILogger<FedresursMessageProcessorService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly FedresursWorkerOptions _options;
    private readonly TimeSpan _pollingInterval;

    public FedresursMessageProcessorService(
        ILogger<FedresursMessageProcessorService> logger,
        IServiceProvider serviceProvider,
        IOptions<FedresursWorkerOptions> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _options = options.Value;

        _pollingInterval = TimeSpan.FromSeconds(_options.ProcessorIntervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Процессор сырых сообщений Федресурса запущен.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNewMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в цикле обработки сообщений.");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }
    }

    private async Task ProcessNewMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

        // Берем пачку непроцесснутых сообщений об объявлении торгов
        var rawMessages = await dbContext.RawFedresursMessages
            .Where(m => !m.IsProcessed && !m.IsLocked && _options.TargetTypes.Contains(m.Type))
            .OrderBy(m => m.CreatedAt)
            .Take(_options.ProcessorBatchSize)
            .ToListAsync(stoppingToken);

        if (!rawMessages.Any()) return;

        foreach (var raw in rawMessages)
        {
            try
            {
                raw.ProcessingAttempts++;

                if (string.IsNullOrWhiteSpace(raw.Content))
                {
                    throw new Exception("Контент сообщения пуст.");
                }

                // Парсим XML контент
                var xmlDoc = XDocument.Parse(raw.Content);

                // Ищем узел Auction или Auction2
                var auctionNode = xmlDoc.Descendants().FirstOrDefault(x => x.Name.LocalName.StartsWith("Auction"));
                if (auctionNode == null)
                {
                    throw new Exception("В XML не найден узел Auction или Auction2.");
                }

                // Извлекаем площадку (TradeSite)
                var platform = (string?)auctionNode.Elements().FirstOrDefault(x => x.Name.LocalName == "TradeSite") ?? "Не указана";

                // Извлекаем тип торгов (TradeType)
                var rawTradeType = (string?)auctionNode.Elements().FirstOrDefault(x => x.Name.LocalName == "TradeType");
                var tradeType = BiddingTypeMapper.GetRussianName(rawTradeType);

                // Собираем лоты
                var lots = new List<Lot>();
                var lotTable = auctionNode.Elements().FirstOrDefault(x => x.Name.LocalName == "LotTable");

                // Ищем AuctionLot или просто Lot (на случай старых версий)
                var lotNodes = lotTable?.Elements().Where(x => x.Name.LocalName.Contains("Lot")) ?? Enumerable.Empty<XElement>();

                foreach (var lotNode in lotNodes)
                {
                    var orderStr = (string?)lotNode.Elements().FirstOrDefault(x => x.Name.LocalName == "Order");
                    var descStr = (string?)lotNode.Elements().FirstOrDefault(x => x.Name.LocalName == "Description");
                    var priceStr = (string?)lotNode.Elements().FirstOrDefault(x => x.Name.LocalName == "StartPrice");
                    var stepStr = (string?)lotNode.Elements().FirstOrDefault(x => x.Name.LocalName == "Step");
                    var advanceStr = (string?)lotNode.Elements().FirstOrDefault(x => x.Name.LocalName == "Advance");

                    lots.Add(new Lot
                    {
                        LotNumber = orderStr ?? "1",
                        Description = descStr ?? "Без описания",
                        StartPrice = ParseDecimalSafe(priceStr),
                        Step = ParseDecimalSafe(stepStr),
                        Deposit = ParseDecimalSafe(advanceStr),
                        CreatedAt = DateTime.UtcNow
                    });
                }

                // Сохраняем в базу (идемпотентность)
                var existingBidding = await dbContext.Biddings.FindAsync(raw.Guid);
                if (existingBidding == null)
                {
                    var bidding = new Bidding
                    {
                        Id = raw.Guid,
                        TradeNumber = raw.Number,
                        Type = tradeType,
                        Platform = platform,
                        AnnouncedAt = raw.DatePublish,
                        Lots = lots,
                        HasNoLots = !lots.Any(),
                        CreatedAt = DateTime.UtcNow
                    };

                    // TODO: Здесь же можно достать <Bankrupt> и <CaseNumber> из корня xmlDoc 
                    // и сохранить их, как ты делал в старом парсере.

                    dbContext.Biddings.Add(bidding);
                }

                // Отмечаем как успешно обработанное
                raw.IsProcessed = true;
                raw.ProcessingError = null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при обработке сообщения {Guid}", raw.Guid);
                raw.ProcessingError = ex.Message;
            }
        }

        await dbContext.SaveChangesAsync(stoppingToken);
        _logger.LogInformation("Обработана пачка из {Count} сообщений.", rawMessages.Count);
    }

    // Вспомогательный метод для надежного парсинга денег (с точками и запятыми)
    private decimal ParseDecimalSafe(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0m;
        var normalized = input.Replace(',', '.');
        decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result);
        return result;
    }
}