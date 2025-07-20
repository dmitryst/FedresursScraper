namespace FedResursScraper
{
    /// <summary>
    /// Лот
    /// </summary>
    class LotInfo
    {
        /// <summary>
        /// Категории
        /// </summary>
        public List<string> Categories { get; set; }

        /// <summary>
        /// Наименование
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Начальная цена
        /// </summary>
        public string StartPrice { get; set; }

        /// <summary>
        /// Шаг цены
        /// </summary>
        public string Step { get; set; }

        /// <summary>
        /// Ссылка на источник
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Задаток
        /// </summary>
        public string Deposit { get; set; }

        /// <summary>
        /// Порядок ознакомления с имуществом
        /// </summary>
        public string ViewingProcedure { get; set; }
    }
}