using FedresursScraper.Services.Enrichments;
using Xunit;

namespace FedresursScraper.Tests.Services;

public class RadHtmlParserTests
{
    [Theory]
    [InlineData("960000333832", "960000333832")]
    [InlineData("0960000333832", "960000333832")]
    public void NormalizeEfrsbLotId_StripsLeadingZeros(string input, string expected)
    {
        Assert.Equal(expected, RadHtmlParser.NormalizeEfrsbLotId(input));
    }

    [Fact]
    public void ParseCatalogItems_ExtractsProductIdTitleLotNumberAndStatus()
    {
        var html = @"
<html><body>
<div class='ty-compact-list__content compact-list-lot-info'>
  <div class='lot-info-sku'>
    <span id='product_code_1759628'>РАД-454052</span>
  </div>
  <div class='lot-info-name'>
    <a href='https://catalog.lot-online.ru/index.php?dispatch=products.view&amp;product_id=1759628'
       class='product-title' title='Лот №11, Картина'>Лот №11, Картина</a>
  </div>
  <div class='lot-info-status'>
    <div class='list-lot-info-status'><span>Опубликована</span></div>
  </div>
</div>
<div class='ty-compact-list__content'>
  <a href='/index.php?dispatch=products.view&product_id=1759476' class='product-title'
     title='Имущество по адресу'>Имущество</a>
  <div class='list-lot-info-status'>Идет прием заявок</div>
</div>
</body></html>";

        var items = RadHtmlParser.ParseCatalogItems(html);

        Assert.Equal(2, items.Count);

        Assert.Equal(1759628, items[0].ProductId);
        Assert.Equal("11", items[0].LotNumber);
        Assert.Equal("РАД-454052", items[0].LotCode);
        Assert.Contains("product_id=1759628", items[0].LotUrl);
        Assert.Equal("Опубликована", items[0].Status);

        Assert.Equal(1759476, items[1].ProductId);
        Assert.Null(items[1].LotNumber);
        Assert.Equal("Идет прием заявок", items[1].Status);
    }

    [Fact]
    public void ExtractEfrsbLotId_FromEAuctionFragment()
    {
        var html = @"
<html><body>
Для участия в процедуре необходима электронная подпись
Идентификатор лота в ЕФРСБ 960000333832
Начальная цена 5 355 255
</body></html>";

        Assert.Equal("960000333832", RadHtmlParser.ExtractEfrsbLotId(html));
    }

    [Fact]
    public void ExtractLotUnid_FromProductPageScript()
    {
        var html = @"
<script>
const url = '/e-auction/auctionLotProperty.v.xhtml?parm=lotUnid%3D960000543467%3Bmode%3Djust';
</script>";

        Assert.Equal("960000543467", RadHtmlParser.ExtractLotUnid(html));
    }

    [Fact]
    public void ExtractImageUrls_FromCmImagePreviewer()
    {
        var html = @"
<html><body>
<a class='cm-image-previewer' href='https://catalog.lot-online.ru/cdn/bkr/img_1563089_3.jpg?t=1&i=1563089'>
  <img src='/thumb.jpg'/>
</a>
<a class='cm-image-previewer' href='https://catalog.lot-online.ru/cdn/bkr/ghallery_1563090_6.jpg'>x</a>
<img src='https://catalog.lot-online.ru/images/detailed/534/Транспорт.png'/>
</body></html>";

        var urls = RadHtmlParser.ExtractImageUrls(html);

        Assert.Equal(2, urls.Count);
        Assert.Contains(urls, u => u.Contains("img_1563089"));
        Assert.Contains(urls, u => u.Contains("ghallery_1563090"));
        Assert.DoesNotContain(urls, u => u.Contains("/images/detailed/534/"));
    }

    [Fact]
    public void ExtractPriceSchedule_ParsesReductionTable()
    {
        var html = @"
<html><body>
<table>
<tr>
  <th>Время начала периода, начала приема заявок</th>
  <th>Время окончания приема заявок</th>
  <th>Время окончания периода</th>
  <th>Величина изменения</th>
  <th>Предложение</th>
  <th>Сумма задатка</th>
</tr>
<tr>
  <td>20.07.2026 17:00</td>
  <td>27.07.2026 17:00</td>
  <td>27.07.2026 17:00</td>
  <td>0.00</td>
  <td>5 355 255.00</td>
  <td>535 525.50</td>
</tr>
<tr>
  <td>27.07.2026 17:00</td>
  <td>01.08.2026 17:00</td>
  <td>01.08.2026 17:00</td>
  <td>481 972.95</td>
  <td>4 873 282.05</td>
  <td>487 328.21</td>
</tr>
</table>
</body></html>";

        var rows = RadHtmlParser.ExtractPriceSchedule(html);

        Assert.Equal(2, rows.Count);
        Assert.Equal(5355255.00m, rows[0].Price);
        Assert.Equal(535525.50m, rows[0].Deposit);
        Assert.Equal(4873282.05m, rows[1].Price);
        Assert.True(rows[0].StartDate < rows[0].EndDate);
    }

    [Fact]
    public void IsFinishedStatus_DetectsTerminalStatuses()
    {
        Assert.True(RadHtmlParser.IsFinishedStatus("Завершена"));
        Assert.True(RadHtmlParser.IsFinishedStatus("Не состоялась"));
        Assert.False(RadHtmlParser.IsFinishedStatus("Опубликована"));
        Assert.False(RadHtmlParser.IsFinishedStatus("Идет прием заявок"));
    }
}
