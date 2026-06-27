using OpenQA.Selenium;
using FedresursScraper.Services.Models;

public interface ILotsScraperFromBankruptMessagePage
{
    Task<BankruptMessageScrapeResult> ScrapeAsync(IWebDriver driver, Guid bankruptMessageId);
}