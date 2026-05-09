using OpenQA.Selenium.Chrome;

public interface IWebDriverFactory
{
    ChromeDriver CreateDriver();
}