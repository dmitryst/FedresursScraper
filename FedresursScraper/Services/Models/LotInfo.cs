namespace FedresursScraper.Services.Models
{
    /// <summary>
    /// Лот, распарсенный с сайта fedresurs
    /// </summary>
    public class LotInfo
    {
        /// <summary>
        /// Номер лота
        /// </summary>
        public string Number { get; set; } = default!;

        /// <summary>
        /// Категории (Классификатор)
        /// </summary>
        public List<string> Categories { get; set; } = default!;

        /// <summary>
        /// Описание
        /// </summary>
        public string Description { get; set; } = default!;

        /// <summary>
        /// Начальная цена
        /// </summary>
        public decimal? StartPrice { get; set; }

        /// <summary>
        /// Шаг цены
        /// </summary>
        public decimal? Step { get; set; }

        /// <summary>
        /// Задаток
        /// </summary>
        public decimal? Deposit { get; set; }

        /// <summary>
        /// Кадастровые номера
        /// </summary>
        public List<string>? CadastralNumbers { get; set; }
    }
}