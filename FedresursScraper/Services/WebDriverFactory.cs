using Microsoft.Extensions.Logging;
using OpenQA.Selenium.Chrome;
using System;

public class WebDriverFactory : IWebDriverFactory
{
    private readonly ILogger<WebDriverFactory> _logger;

    public WebDriverFactory(ILogger<WebDriverFactory> logger)
    {
        _logger = logger;
    }

    public ChromeDriver CreateDriver()
    {
        // === КОНФИГУРАЦИЯ ОПЦИЙ CHROME ===
        var options = new ChromeOptions();
        
        // НАИБОЛЕЕ ПОЛНЫЙ НАБОР АРГУМЕНТОВ ДЛЯ DOCKER
        options.AddArgument("--headless=new");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--disable-gpu");
        options.AddArgument("--window-size=1920,1080");
        options.AddArgument("--disable-extensions");
        options.AddArgument("--disable-infobars");
        options.AddArgument("--remote-debugging-port=9222");

        // УНИКАЛЬНЫЙ ПРОФИЛЬ ПОЛЬЗОВАТЕЛЯ
        var userDataDir = $"/tmp/chrome-profile-{Guid.NewGuid()}";
        options.AddArgument($"--user-data-dir={userDataDir}");

        // НАСТРОЙКИ ДЛЯ МАСКИРОВКИ
        options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36");
        options.AddExcludedArgument("enable-automation");
        options.AddAdditionalOption("useAutomationExtension", false);
        
        return new ChromeDriver(options);
    }
}