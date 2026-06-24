using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FedresursScraper.Services;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;
using static FedresursScraper.Services.CdtEnrichmentService;

namespace FedresursScraper.Tests.Services;

public class CdtEnrichmentServiceTests : IDisposable
{
    private readonly LotsDbContext _dbContext;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILotsFileStorageService> _fileStorageMock;
    private readonly Mock<ILogger<CdtEnrichmentService>> _loggerMock;

    public CdtEnrichmentServiceTests()
    {
        var options = new DbContextOptionsBuilder<LotsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TestLotsDbContext(options);

        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);

        _fileStorageMock = new Mock<ILotsFileStorageService>();
        _loggerMock = new Mock<ILogger<CdtEnrichmentService>>();
    }

    [Fact]
    public async Task EnrichByTradeNumberAsync_ValidJson_ParsesImagesAndSchedules()
    {
        // Arrange
        var tradeNumber = "350689";
        
        var bidding = new Bidding
        {
            Id = Guid.NewGuid(),
            TradeNumber = tradeNumber,
            Platform = "Центр дистанционных торгов",
            Type = "Публичное предложение", // Чтобы парсился график
            IsEnriched = false,
            Lots = new List<Lot>
            {
                new Lot { Id = Guid.NewGuid(), LotNumber = "1" }
            }
        };

        _dbContext.Biddings.Add(bidding);
        await _dbContext.SaveChangesAsync();

        var json = @"{
          ""lot"": {
            ""tradeId"": 350689,
            ""tradeLotId"": 351218,
            ""lotScheduleItems"": [
              {
                ""startTime"": ""06.06.2026 10:00:00"",
                ""endTime"": ""10.06.2026 18:00:00"",
                ""price"": ""2837774,70""
              }
            ],
            ""images"": [
              {
                ""id"": ""93308c54-1118-4bed-b68e-c95fa8d113a6"",
                ""position"": 0,
                ""isMain"": true
              }
            ]
          }
        }";

        // Mock public API request (primary source since Nuxt migration)
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains($"Trade/public/{tradeNumber}")),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json)
            });

        // Mock image download request
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains("LotImage/public")),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
            });

        _fileStorageMock
            .Setup(x => x.UploadAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("https://s3.url/image.jpg");

        var service = new CdtEnrichmentService(_dbContext, _httpClient, _fileStorageMock.Object, _loggerMock.Object);

        // Act
        await service.EnrichByTradeNumberAsync(tradeNumber, CancellationToken.None);

        // Assert
        var updatedBidding = await _dbContext.Biddings
            .Include(b => b.Lots).ThenInclude(l => l.Images)
            .Include(b => b.Lots).ThenInclude(l => l.PriceSchedules)
            .FirstAsync(b => b.Id == bidding.Id);

        Assert.True(updatedBidding.IsEnriched);
        Assert.NotNull(updatedBidding.EnrichedAt);
        
        var lot = updatedBidding.Lots.First();
        Assert.Single(lot.Images);
        Assert.Equal("https://s3.url/image.jpg", lot.Images.First().Url);
        
        Assert.Single(lot.PriceSchedules);
        var schedule = lot.PriceSchedules.First();
        Assert.Equal(new DateTime(2026, 6, 6, 7, 0, 0, DateTimeKind.Utc), schedule.StartDate);
        Assert.Equal(new DateTime(2026, 6, 10, 15, 0, 0, DateTimeKind.Utc), schedule.EndDate);
        Assert.Equal(2837774.70m, schedule.Price);
    }

    [Fact]
    public async Task EnrichByTradeNumberAsync_NoJson_CompletesWithoutErrorAndSetsIsEnriched()
    {
        // Arrange
        var tradeNumber = "12345";
        
        var bidding = new Bidding
        {
            Id = Guid.NewGuid(),
            TradeNumber = tradeNumber,
            Platform = "Центр дистанционных торгов",
            Type = "Публичное предложение",
            IsEnriched = false,
            Lots = new List<Lot>
            {
                new Lot { Id = Guid.NewGuid(), LotNumber = "1" }
            }
        };

        _dbContext.Biddings.Add(bidding);
        await _dbContext.SaveChangesAsync();

        // No pre tag in html; API returns 404
        var html = $@"<html><body><div>No images here</div></body></html>";

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains($"Trade/public/{tradeNumber}")),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound,
                Content = new StringContent("")
            });

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains($"trades/{tradeNumber}")),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(html)
            });

        var service = new CdtEnrichmentService(_dbContext, _httpClient, _fileStorageMock.Object, _loggerMock.Object);

        // Act
        await service.EnrichByTradeNumberAsync(tradeNumber, CancellationToken.None);

        // Assert
        var updatedBidding = await _dbContext.Biddings
            .Include(b => b.Lots).ThenInclude(l => l.Images)
            .FirstAsync(b => b.Id == bidding.Id);

        // Requirement 3: Если картинки не были найдены, то повторно запускать парсинг данной странице не нужно.
        // It should be marked as enriched so it won't be retried.
        Assert.True(updatedBidding.IsEnriched);
        Assert.NotNull(updatedBidding.EnrichedAt);
        Assert.Empty(updatedBidding.Lots.First().Images);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _httpClient.Dispose();
    }
}