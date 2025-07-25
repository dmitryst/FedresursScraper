namespace FedResursScraper
{
    /// <summary>
    /// Лот
    /// </summary>
    class LotInfo
    {
        /// <summary>
        /// Вид торгов
        /// </summary>
        public string BiddingType  { get; set; }

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
        public decimal? StartPrice { get; set; }

        /// <summary>
        /// Шаг цены
        /// </summary>
        public decimal? Step { get; set; }

        /// <summary>
        /// Ссылка на источник
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Задаток
        /// </summary>
        public decimal? Deposit { get; set; }

        /// <summary>
        /// Порядок ознакомления с имуществом
        /// </summary>
        public string ViewingProcedure { get; set; }
    }
}