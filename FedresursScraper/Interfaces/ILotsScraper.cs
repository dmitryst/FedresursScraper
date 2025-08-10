using OpenQA.Selenium;
using FedresursScraper.Services.Models;

public interface ILotsScraper
{
    Task<List<LotInfo>> ScrapeLotsAsync(IWebDriver driver, Guid bankruptMessageId);
}