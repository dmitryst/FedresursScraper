namespace FedresursScraper.Services.Models
{
    /// <summary>
    /// Лот
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
        /// Ссылка на источник
        /// </summary>
        // public string? Url { get; set; }

        /// <summary>
        /// Кадастровые номера
        /// </summary>
        public List<string>? CadastralNumbers { get; set; }

        /// <summary>
        /// Координаты участка (ширина)
        /// </summary>
        public double? Latitude { get; set; }

        /// <summary>
        /// Координаты участка (долгота)
        /// </summary>
        public double? Longitude { get; set; }
    }
}