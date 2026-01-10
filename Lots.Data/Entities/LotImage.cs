namespace Lots.Data.Entities
{
    /// <summary>
    /// Фото лота
    /// </summary>
    public class LotImage
    {
        public Guid Id { get; set; }
        public Guid LotId { get; set; }
        public Lot Lot { get; set; } = default!;

        /// <summary>
        /// Ссылка на S3 или внешний источник
        /// </summary>
        public string Url { get; set; } = default!;

        /// <summary>
        /// Порядок сортировки
        /// </summary>
        public int Order { get; set; }
    }
}