namespace FedresursScraper.Integrations.Fedresurs.Models;

    public class FedresursWorkerOptions
    {
        // --- Настройки Агрегатора (Сбор данных) ---
        
        /// <summary>
        /// Как часто опрашивать API (в секундах)
        /// </summary>
        public int AggregatorIntervalSeconds { get; set; } = 900; 
        
        /// <summary>
        /// За сколько часов назад запрашивать данные, если БД пуста
        /// </summary>
        public int InitialFetchHoursBack { get; set; } = 72; 
        
        /// <summary>
        /// Сколько записей запрашивать за один HTTP-запрос к API (до 500)
        /// </summary>
        public int ApiRequestLimit { get; set; } = 500;
        
        /// <summary>
        /// Какие типы сообщений нас интересуют
        /// </summary>
        public string[] TargetTypes { get; set; } = { "Auction", "Auction2" };

        // --- Настройки Процессора (Бизнес-логика) ---
        
        /// <summary>
        /// Как часто проверять базу на наличие новых сырых сообщений
        /// </summary>
        public int ProcessorIntervalSeconds { get; set; } = 30;
        
        /// <summary>
        /// По сколько сырых сообщений забирать из БД за один раз для парсинга
        /// </summary>
        public int ProcessorBatchSize { get; set; } = 50;
    }