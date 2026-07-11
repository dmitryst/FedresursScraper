using System.Net;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace FedresursScraper.Services.Enrichments;

/// <summary>
/// Навигация по Альфалот через Chrome: проходит JS-challenge InProtect.
/// </summary>
public static class AlfalotSeleniumNavigator
{
    public static void OpenAndWait(IWebDriver driver, string url, string? waitCssSelector, TimeSpan timeout)
    {
        driver.Navigate().GoToUrl(url);
        WaitUntilReady(driver, waitCssSelector, timeout);
    }

    public static void WaitUntilReady(IWebDriver driver, string? waitCssSelector, TimeSpan timeout)
    {
        var wait = new WebDriverWait(driver, timeout)
        {
            PollingInterval = TimeSpan.FromMilliseconds(750)
        };
        wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException), typeof(NoSuchElementException));

        wait.Until(d =>
        {
            var html = d.PageSource ?? string.Empty;
            if (AlfalotHtmlParser.IsWafChallenge(html))
                return false;

            if (string.IsNullOrWhiteSpace(waitCssSelector))
                return html.Length > 2000;

            return d.FindElements(By.CssSelector(waitCssSelector)).Count > 0;
        });
    }

    public static string GetPageHtml(IWebDriver driver)
    {
        var html = driver.PageSource ?? string.Empty;
        if (AlfalotHtmlParser.IsWafChallenge(html))
        {
            throw new InvalidOperationException(
                "Альфалот всё ещё отдаёт InProtect challenge после ожидания в браузере.");
        }

        return html;
    }

    public static bool TryGoToCatalogPage(IWebDriver driver, int pageNumber, TimeSpan timeout)
    {
        var currentSpans = driver.FindElements(
            By.XPath("//td[contains(@class,'pager')]//span[normalize-space()='" + pageNumber + "']"));
        if (currentSpans.Count > 0)
            return true;

        var links = driver.FindElements(
            By.XPath("//td[contains(@class,'pager')]//a[normalize-space()='" + pageNumber + "']"));
        if (links.Count == 0)
        {
            links = driver.FindElements(
                By.XPath("//td[contains(@class,'pager')]//a[contains(.,'>>') or normalize-space()='»']"));
            if (links.Count == 0)
                return false;
        }

        links[0].Click();
        WaitUntilReady(driver, "tr.gridRow, tr.gridAltRow", timeout);
        return true;
    }

    public static CookieContainer ExportCookies(IWebDriver driver, string domainHost = "bankrupt.alfalot.ru")
    {
        var container = new CookieContainer();
        foreach (var cookie in driver.Manage().Cookies.AllCookies)
        {
            try
            {
                var domain = string.IsNullOrWhiteSpace(cookie.Domain)
                    ? domainHost
                    : cookie.Domain.TrimStart('.');

                var path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path;
                container.Add(new System.Net.Cookie(cookie.Name, cookie.Value, path, domain));
            }
            catch
            {
                // пропускаем cookies, которые System.Net не принимает
            }
        }

        return container;
    }
}
