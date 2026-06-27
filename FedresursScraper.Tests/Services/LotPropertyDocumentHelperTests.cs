using FedresursScraper.Services;
using Xunit;

namespace FedresursScraper.Tests.Services;

public class LotPropertyDocumentHelperTests
{
    [Fact]
    public void IsJunkAttachment_InterfaxPolicy_ReturnsTrue()
    {
        var title = "Политика АО «Интерфакс» в отношении обработки и защиты персональных данных.pdf";
        var url = "https://www.interfax.ru/.../policy.pdf";

        Assert.True(LotPropertyDocumentHelper.IsJunkAttachment(title, url));
    }

    [Fact]
    public void IsJunkAttachment_FedresursLotDocument_ReturnsFalse()
    {
        var title = "Состав лота.docx";
        var url = "https://fedresurs.ru/filestorage/abc/Состав%20лота.docx";

        Assert.False(LotPropertyDocumentHelper.IsJunkAttachment(title, url));
    }

    [Theory]
    [InlineData("Состав лота.docx", "https://fedresurs.ru/file.docx", ".docx", true)]
    [InlineData("Проект ДКП движимое (2).docx", "https://fedresurs.ru/file.docx", ".docx", true)]
    [InlineData("Договор о задатке.rar", "https://fedresurs.ru/file.rar", ".rar", true)]
    [InlineData("Политика Интерфакс.pdf", "https://fedresurs.ru/policy.pdf", ".pdf", false)]
    public void IsLikelyTradeDocumentLink_FiltersExpected(
        string title,
        string url,
        string extension,
        bool expected)
    {
        Assert.Equal(expected, LotPropertyDocumentHelper.IsLikelyTradeDocumentLink(title, url, extension));
    }
}
