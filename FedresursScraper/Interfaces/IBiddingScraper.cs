using OpenQA.Selenium;
using FedresursScraper.Services.Models;

public interface IBiddingScraper
{
    Task<BiddingInfo> ScrapeDataAsync(IWebDriver driver, Guid biddingId);
}