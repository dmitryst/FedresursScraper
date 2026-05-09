using OpenQA.Selenium;
using FedresursScraper.Services.Models;

public interface IBiddingScraper
{
    Task<BiddingInfo> ScrapeBiddingInfoAsync(IWebDriver driver, Guid biddingId);
}