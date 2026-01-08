using OpenQA.Selenium;
using FedresursScraper.Services.Models;

public interface ILotsScraperFromBankruptMessagePage
{
    Task<List<LotInfo>> ScrapeLotsAsync(IWebDriver driver, Guid bankruptMessageId);
}