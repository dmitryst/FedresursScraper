using OpenQA.Selenium;

namespace FedResursScraper
{
    public interface IScraperService
    {
        Task<LotInfo> ScrapeLotData(IWebDriver driver, string lotUrl);
    }
}