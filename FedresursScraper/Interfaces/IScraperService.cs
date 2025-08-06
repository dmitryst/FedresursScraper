using OpenQA.Selenium;

public interface IScraperService
{
    Task<LotInfo> ScrapeLotData(IWebDriver driver, string lotUrl);
}