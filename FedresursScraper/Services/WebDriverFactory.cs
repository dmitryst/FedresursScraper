using Microsoft.Extensions.Logging;
using OpenQA.Selenium.Chrome;
using System;

namespace FedresursScraper.Services
{
    public class WebDriverFactory : IWebDriverFactory
    {
        private readonly ILogger<WebDriverFactory> _logger;

        public WebDriverFactory(ILogger<WebDriverFactory> logger)
        {
            _logger = logger;
        }

        public ChromeDriver CreateDriver()
        {
            var options = new ChromeOptions();

            // --- ОБЩИЕ НАСТРОЙКИ, КОТОРЫЕ НУЖНЫ ВСЕГДА ---
            options.AddArgument("--window-size=1920,1080");
            options.AddArgument("--disable-extensions");
            options.AddArgument("--disable-infobars");
            options.AddArgument("--disable-gpu");

#if DEBUG
            _logger.LogInformation("Applying DEBUG options for local development.");
            // Опции для локальной отладки (без headless)
#else
                _logger.LogInformation("Applying RELEASE options for Docker/Production.");
                
                options.AddArgument("--headless=new");
                options.AddArgument("--no-sandbox");            // Критически важный флаг для запуска от root в Docker
                options.AddArgument("--disable-dev-shm-usage"); // Предотвращает проблемы с общей памятью /dev/shm в Docker
                options.AddArgument("--remote-debugging-port=9222");
#endif
            // УНИКАЛЬНЫЙ ПРОФИЛЬ ПОЛЬЗОВАТЕЛЯ
            // Создаем кросс-платформенный путь к временной папке
            var tempPath = Path.GetTempPath();
            var profileDir = Path.Combine(tempPath, $"chrome-profile-{Guid.NewGuid()}");

            // Создаем директорию, если она не существует (на всякий случай)
            Directory.CreateDirectory(profileDir);

            options.AddArgument($"--user-data-dir={profileDir}");

            // Настройки для маскировки под обычного пользователя
            options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36");
            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", false);

            try
            {
                _logger.LogInformation("Attempting to instantiate ChromeDriver with the specified options.");
                var driver = new ChromeDriver(options);
                _logger.LogInformation("ChromeDriver instance created successfully. Session ID: {SessionId}", driver.SessionId);
                return driver;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "FATAL: Failed to create ChromeDriver session. Please check Chrome/ChromeDriver version compatibility and system resource availability (RAM).");
                throw;
            }
        }
    }
}