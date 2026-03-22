using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FedresursScraper.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Polly;
using Xunit;

namespace FedresursScraper.Tests.Services;

public class RosreestrServiceClientTests
{
    [Fact]
    public async Task GetCadastralInfoAsync_FirstExample_ParsesAllFieldsCorrectly()
    {
        // Arrange
        var expectedCadastralNumber = "47:29:1025006:2";

        // Считываем JSON из файла
        var geoJson = File.ReadAllText("TestData/47_29_1025006_2.geojson");

        var service = CreateClientWithMockedResponse(geoJson);

        // Act
        var result = await service.GetCadastralInfoAsync(expectedCadastralNumber);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedCadastralNumber, result.CadastralNumber);
        Assert.Equal(660, result.Area);
        Assert.Equal(89779.8m, result.CadastralCost);
        Assert.Equal("Земли сельскохозяйственного назначения", result.Category);
        Assert.Equal("для ведения садоводства", result.PermittedUse);
        Assert.Equal("Ленинградская область, Лужский район, Мшинское сельское поселение, массив Дивенская, с.т. Мелиоратор, уч.253", result.Address);
        Assert.Equal("Ранее учтенный", result.Status);
        Assert.Equal("Земельный участок", result.ObjectType);
        Assert.Null(result.RightType); // В первом примере поля right_type нет
        Assert.Equal("Частная", result.OwnershipType);
        Assert.Equal("2002-08-08", result.RegDate);
    }

    [Fact]
    public async Task GetCadastralInfoAsync_SecondExample_ParsesAllFieldsCorrectly()
    {
        // Arrange
        var expectedCadastralNumber = "39:10:480001:58";

        // Считываем JSON из файла (предполагаем, что вы назовете его так)
        var geoJson = File.ReadAllText("TestData/39_10_480001_58.geojson");

        var service = CreateClientWithMockedResponse(geoJson);

        // Act
        var result = await service.GetCadastralInfoAsync(expectedCadastralNumber);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedCadastralNumber, result.CadastralNumber);
        Assert.Equal(1200, result.Area);
        Assert.Equal(441072m, result.CadastralCost);
        Assert.Equal("Земли населенных пунктов", result.Category);
        Assert.Equal("под строительство индивидуального жилого дома", result.PermittedUse);
        Assert.Equal("Калининградская область, Полесский район, пос. Красное, ул. Куршская, д. 3 в", result.Address);
        Assert.Equal("Учтенный", result.Status);
        Assert.Equal("Земельный участок", result.ObjectType);
        Assert.Equal("Собственность", result.RightType);
        Assert.Equal("Частная", result.OwnershipType);
        Assert.Equal("2009-06-19", result.RegDate);
    }

    // =========================================================================
    // Вспомогательный метод для подмены HttpClient и ILogger
    // =========================================================================
    private RosreestrServiceClient CreateClientWithMockedResponse(string jsonResponse)
    {
        // Подменяем HttpMessageHandler для эмуляции ответа
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse)
            });

        // Создаем HttpClient с нашим поддельным обработчиком
        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost/")
        };

        // Создаем "пустой" логгер (заглушку), чтобы конструктор не ругался
        var mockLogger = Mock.Of<ILogger<RosreestrServiceClient>>();

        // Возвращаем готовый клиент
        return new RosreestrServiceClient(httpClient, mockLogger);
    }
}