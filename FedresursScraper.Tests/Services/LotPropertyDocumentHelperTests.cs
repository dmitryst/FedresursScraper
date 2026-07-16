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

    [Theory]
    [InlineData("Договор о задатке.docx", PropertyDocumentType.ContractOrTemplate)]
    [InlineData("Проект ДКП движимое.docx", PropertyDocumentType.ContractOrTemplate)]
    [InlineData("Соглашение о задатке.pdf", PropertyDocumentType.ContractOrTemplate)]
    [InlineData("Положение о торгах.docx", PropertyDocumentType.ContractOrTemplate)]
    [InlineData("Перечень имущества лота.docx", PropertyDocumentType.PropertyList)]
    [InlineData("Состав лота №3.docx", PropertyDocumentType.PropertyList)]
    [InlineData("Описание имущества.docx", PropertyDocumentType.PropertyList)]
    [InlineData("Приложение_1.docx", PropertyDocumentType.Unknown)]
    [InlineData("файл.docx", PropertyDocumentType.Unknown)]
    public void DetermineDocumentTypeByTitle_ReturnsExpectedType(string title, PropertyDocumentType expected)
    {
        Assert.Equal(expected, LotPropertyDocumentHelper.DetermineDocumentTypeByTitle(title));
    }

    [Fact]
    public void DetermineDocumentTypeByContent_DetectsDepositAgreement()
    {
        var text =
            "СОГЛАШЕНИЕ О ЗАДАТКЕ\n\nНастоящий договор заключён между сторонами. " +
            "Задаток составляет 100 000 рублей. Предмет договора — участие в торгах.";

        Assert.Equal(
            PropertyDocumentType.ContractOrTemplate,
            LotPropertyDocumentHelper.DetermineDocumentTypeByContent(text));
    }

    [Fact]
    public void DetermineDocumentTypeByContent_DetectsPropertyList()
    {
        var text =
            "Перечень имущества, включённого в состав лота №1\n\n" +
            "1. Наименование имущества: станок токарный\n" +
            "2. Инвентарный номер: 12345\n" +
            "Балансовая стоимость: 50000";

        Assert.Equal(
            PropertyDocumentType.PropertyList,
            LotPropertyDocumentHelper.DetermineDocumentTypeByContent(text));
    }

    [Fact]
    public void DetermineDocumentTypeByContent_ContractWinsOverPropertyMentions()
    {
        var text =
            "ДОГОВОР КУПЛИ-ПРОДАЖИ\nПредмет договора: имущество должника согласно перечню имущества. " +
            "Права и обязанности сторон определяются настоящим договором.";

        Assert.Equal(
            PropertyDocumentType.ContractOrTemplate,
            LotPropertyDocumentHelper.DetermineDocumentTypeByContent(text));
    }

    [Fact]
    public void DetermineDocumentType_TitleContract_SkipsContent()
    {
        var documentType = LotPropertyDocumentHelper.DetermineDocumentType(
            "Договор о задатке.docx",
            "Перечень имущества лота: станок, автомобиль");

        Assert.Equal(PropertyDocumentType.ContractOrTemplate, documentType);
    }

    [Fact]
    public void DetermineDocumentType_UnknownTitle_UsesContent()
    {
        var documentType = LotPropertyDocumentHelper.DetermineDocumentType(
            "Приложение_1.docx",
            "Состав лота: 1. Наименование имущества — автомобиль");

        Assert.Equal(PropertyDocumentType.PropertyList, documentType);
    }

    [Fact]
    public void ShouldExtractTextForDescription_FalseForContracts()
    {
        Assert.False(LotPropertyDocumentHelper.ShouldExtractTextForDescription(
            PropertyDocumentType.ContractOrTemplate));
        Assert.True(LotPropertyDocumentHelper.ShouldExtractTextForDescription(
            PropertyDocumentType.PropertyList));
        Assert.True(LotPropertyDocumentHelper.ShouldExtractTextForDescription(
            PropertyDocumentType.Unknown));
    }

    [Fact]
    public void ShouldSummarizeForDescription_OnlyPropertyListsThatNeedIt()
    {
        var longPropertyText = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"{i}. Позиция имущества {i}"));

        Assert.True(LotPropertyDocumentHelper.ShouldSummarizeForDescription(
            PropertyDocumentType.PropertyList, longPropertyText));
        Assert.False(LotPropertyDocumentHelper.ShouldSummarizeForDescription(
            PropertyDocumentType.ContractOrTemplate, longPropertyText));
        Assert.False(LotPropertyDocumentHelper.ShouldSummarizeForDescription(
            PropertyDocumentType.Unknown, longPropertyText));
        Assert.False(LotPropertyDocumentHelper.ShouldSummarizeForDescription(
            PropertyDocumentType.PropertyList, "Короткий текст"));
    }

    [Fact]
    public void GetDefaultUseForDescription_OnlyPropertyListWithText()
    {
        Assert.True(LotPropertyDocumentHelper.GetDefaultUseForDescription(
            PropertyDocumentType.PropertyList, hasExtractedText: true));
        Assert.False(LotPropertyDocumentHelper.GetDefaultUseForDescription(
            PropertyDocumentType.ContractOrTemplate, hasExtractedText: true));
        Assert.False(LotPropertyDocumentHelper.GetDefaultUseForDescription(
            PropertyDocumentType.Unknown, hasExtractedText: true));
        Assert.False(LotPropertyDocumentHelper.GetDefaultUseForDescription(
            PropertyDocumentType.PropertyList, hasExtractedText: false));
    }

    [Theory]
    [InlineData("Проект ДКП.docx", false)]
    [InlineData("Договор о задатке.docx", false)]
    [InlineData("Перечень имущества.docx", true)]
    [InlineData("Приложение.docx", true)]
    public void GetDefaultSelectedForDownload_ByTitle(string title, bool expected)
    {
        Assert.Equal(expected, LotPropertyDocumentHelper.GetDefaultSelectedForDownload(title));
    }
}
