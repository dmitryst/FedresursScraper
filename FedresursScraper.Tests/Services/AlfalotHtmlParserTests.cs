using FedresursScraper.Services.Enrichments;
using Xunit;

namespace FedresursScraper.Tests.Services;

public class AlfalotHtmlParserTests
{
    [Theory]
    [InlineData("0177088", "177088")]
    [InlineData("177088", "177088")]
    [InlineData("0001", "1")]
    public void NormalizeTradeNumber_StripsLeadingZeros(string input, string expected)
    {
        Assert.Equal(expected, AlfalotHtmlParser.NormalizeTradeNumber(input));
    }

    [Fact]
    public void ParseCatalogRows_ExtractsTradeLotUrlsAndDates()
    {
        var html = @"
<html><body>
<table>
<tr class='gridRow'>
  <td class='gridAltColumn'><a href='/public/auctions/view/177090/' class='purchase-type-auction-open'>0177088</a></td>
  <td class='gridColumn'><a href='/public/auctions/view/177090/'>Title</a></td>
  <td class='gridAltColumn'><a href='/public/auctions/lots/view/446279/'>1</a></td>
  <td class='gridColumn'><a href='/public/auctions/lots/view/446279/'>Lot title</a></td>
  <td class='columnCurrency gridAltColumn'>450 000,00</td>
  <td class='gridColumn'>Organizer</td>
  <td class='gridAltColumn'>23.08.2025 00:00</td>
  <td class='columnDateTime gridColumn'>23.08.2025 10:00</td>
  <td class='gridAltColumn'>Объявление опубликовано</td>
  <td class='gridColumn'></td>
  <td class='gridAltColumn'>Открытый аукцион</td>
</tr>
<tr class='gridRow'>
  <td><a href='/public/public-offers/view/99472/'>0163122</a></td>
  <td><a href='/public/public-offers/view/99472/'>PO</a></td>
  <td><a href='/public/public-offers/lots/view/522105/'>2</a></td>
  <td><a href='/public/public-offers/lots/view/522105/'>Lot 2</a></td>
  <td>10 000,00</td>
  <td>Org</td>
  <td>01.01.2020 00:00</td>
  <td>01.01.2020 10:00</td>
  <td>Окончен</td>
  <td></td>
  <td>Публичное предложение</td>
</tr>
</table>
</body></html>";

        var rows = AlfalotHtmlParser.ParseCatalogRows(html);

        Assert.Equal(2, rows.Count);

        Assert.Equal("0177088", rows[0].TradeNumber);
        Assert.Equal("1", rows[0].LotNumber);
        Assert.Equal("https://bankrupt.alfalot.ru/public/auctions/view/177090/", rows[0].TradeUrl);
        Assert.Equal("https://bankrupt.alfalot.ru/public/auctions/lots/view/446279/", rows[0].LotUrl);
        Assert.Equal("Объявление опубликовано", rows[0].Status);
        Assert.False(AlfalotHtmlParser.IsFinishedStatus(rows[0].Status));
        Assert.NotNull(rows[0].ApplicationsEndAt);

        Assert.True(AlfalotHtmlParser.IsFinishedStatus(rows[1].Status));
    }

    [Fact]
    public void ExtractImageUrls_FromPrettyPhotoAndAttachments()
    {
        var html = @"
<html><body>
<a rel='prettyPhoto[gallery]' href='/public/attachments/file/1/photo1.jpg'><img src='/thumb.jpg'/></a>
<table>
<tr class='attachment-grid-row'>
  <td></td><td>1</td><td>01.01.2026</td>
  <td><a href='/public/attachments/file/2/scan.pdf'>scan.pdf</a></td>
  <td></td><td></td><td>Документ</td>
</tr>
<tr class='attachment-grid-row'>
  <td></td><td>2</td><td>01.01.2026</td>
  <td><a href='/public/attachments/file/3/car.png'>car.png</a></td>
  <td></td><td></td><td>Изображение</td>
</tr>
</table>
</body></html>";

        var urls = AlfalotHtmlParser.ExtractImageUrls(html);

        Assert.Contains("https://bankrupt.alfalot.ru/public/attachments/file/1/photo1.jpg", urls);
        Assert.Contains("https://bankrupt.alfalot.ru/public/attachments/file/3/car.png", urls);
        Assert.DoesNotContain(urls, u => u.Contains("scan.pdf"));
    }

    [Fact]
    public void ExtractPriceSchedule_MapsColumnsByHeaders()
    {
        var html = @"
<html><body>
<div id='ctl00_ctl00_MainContent_ContentPlaceHolderMiddle_publicOfferReduction_fsPublicOfferReduction'>
<table>
<tr>
  <td>Дата начала интервала</td>
  <td>Дата начала приема заявок на интервале</td>
  <td>Дата окончания приема заявок на интервале</td>
  <td>Дата окончания интервала</td>
  <td>Снижение от предыдущей цены, рубли</td>
  <td>Задаток на интервале, руб.</td>
  <td>Цена на интервале, руб.</td>
  <td>Комментарий</td>
</tr>
<tr>
  <td>27.03.2025 14:00</td>
  <td>27.03.2025 14:00</td>
  <td>03.04.2025 14:00</td>
  <td>03.04.2025 14:00</td>
  <td>0,00</td>
  <td>8 000,00</td>
  <td>80 000,00</td>
  <td></td>
</tr>
<tr>
  <td>03.04.2025 14:00</td>
  <td>03.04.2025 14:00</td>
  <td>10.04.2025 14:00</td>
  <td>10.04.2025 14:00</td>
  <td>6000,00</td>
  <td>7 400,00</td>
  <td>74 000,00</td>
  <td></td>
</tr>
</table>
</div>
</body></html>";

        var rows = AlfalotHtmlParser.ExtractPriceSchedule(html);

        Assert.Equal(2, rows.Count);
        Assert.Equal(80000m, rows[0].Price);
        Assert.Equal(8000m, rows[0].Deposit);
        Assert.Equal(74000m, rows[1].Price);
        Assert.True(rows[0].StartDate < rows[0].EndDate);
    }

    [Fact]
    public void ExtractPagerTargets_ReadsPageNumbers()
    {
        var html = @"
<td class='pager' colspan='11'>
<span>Страницы:</span><span>1</span>&nbsp;
<a href=""javascript:__doPostBack('ctl00$ctl00$MainContent$ContentPlaceHolderMiddle$PurchasesSearchResult$ctl01$ctl02','')"">2</a>&nbsp;
<a href=""javascript:__doPostBack('ctl00$ctl00$MainContent$ContentPlaceHolderMiddle$PurchasesSearchResult$ctl01$ctl03','')"">3</a>
</td>";

        var pages = AlfalotHtmlParser.ExtractPagerTargets(html);
        Assert.Contains(pages, p => p.PageNumber == 2);
        Assert.Contains(pages, p => p.PageNumber == 3);
        Assert.Equal(1, AlfalotHtmlParser.ExtractCurrentPageNumber(html));
    }
}
