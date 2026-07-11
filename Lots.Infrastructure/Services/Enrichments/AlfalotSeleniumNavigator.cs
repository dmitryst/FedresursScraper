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

    /// <summary>
    /// Текущий номер страницы каталога (из pager: активная страница — span с цифрой).
    /// </summary>
    public static int? ReadCurrentCatalogPage(IWebDriver driver)
    {
        var spans = driver.FindElements(By.XPath("//td[contains(@class,'pager')]//span"));
        foreach (var span in spans)
        {
            try
            {
                var text = span.Text?.Trim();
                if (!string.IsNullOrEmpty(text) && int.TryParse(text, out var page))
                    return page;
            }
            catch (StaleElementReferenceException)
            {
                // ignore
            }
        }

        return AlfalotHtmlParser.ExtractCurrentPageNumber(driver.PageSource ?? string.Empty);
    }

    /// <summary>
    /// Переход на страницу каталога. Возвращает true только если номер страницы реально сменился
    /// (или уже были на целевой).
    /// </summary>
    public static bool TryGoToCatalogPage(IWebDriver driver, int pageNumber, TimeSpan timeout)
    {
        var beforePage = ReadCurrentCatalogPage(driver);
        if (beforePage == pageNumber)
            return true;

        var link = FindPagerLink(driver, pageNumber);
        if (link == null)
            return false;

        try
        {
            ((IJavaScriptExecutor)driver).ExecuteScript(
                "arguments[0].scrollIntoView({block:'center'});", link);
            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", link);
        }
        catch
        {
            try
            {
                link.Click();
            }
            catch
            {
                return false;
            }
        }

        var wait = new WebDriverWait(driver, timeout)
        {
            PollingInterval = TimeSpan.FromMilliseconds(500)
        };
        wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException), typeof(NoSuchElementException));

        try
        {
            wait.Until(d =>
            {
                var html = d.PageSource ?? string.Empty;
                if (AlfalotHtmlParser.IsWafChallenge(html))
                    return false;

                if (d.FindElements(By.CssSelector("tr.gridRow, tr.gridAltRow")).Count == 0)
                    return false;

                var now = ReadCurrentCatalogPage(d);
                // Успех — номер страницы реально сменился (точный pageNumber или скачок через >>).
                return now.HasValue && now.Value != beforePage;
            });

            var afterPage = ReadCurrentCatalogPage(driver);
            return afterPage.HasValue && afterPage.Value != beforePage;
        }
        catch (WebDriverTimeoutException)
        {
            return false;
        }
    }

    private static IWebElement? FindPagerLink(IWebDriver driver, int pageNumber)
    {
        var exact = driver.FindElements(
            By.XPath("//td[contains(@class,'pager')]//a[normalize-space()='" + pageNumber + "']"));
        if (exact.Count > 0)
            return exact[^1]; // нижний pager надёжнее

        // Если нужной страницы нет в видимом окне (1..10), жмём >>
        var nextBlock = driver.FindElements(
            By.XPath("//td[contains(@class,'pager')]//a[normalize-space()='>>' or normalize-space()='»' or contains(.,'>>')]"));
        if (nextBlock.Count > 0)
            return nextBlock[^1];

        return null;
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
