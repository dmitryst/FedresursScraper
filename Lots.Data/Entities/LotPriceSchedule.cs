namespace Lots.Data.Entities
{
    /// <summary>
    /// График снижения цены
    /// </summary>
    public class LotPriceSchedule
    {
        public Guid Id { get; set; }
        public Guid LotId { get; set; }
        public Lot Lot { get; set; } = default!;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        /// <summary>
        /// Минимальная цена
        /// </summary>
        public decimal Price { get; set; }

        /// <summary>
        /// Задаток
        /// </summary>
        public decimal Deposit { get; set; }

        /// <summary>
        /// Ранг
        /// </summary>
        public double? EstimatedRank { get; set; }

        /// <summary>
        /// ROI текущего этапа
        /// </summary>
        public double? PotentialRoi { get; set; }
    }
}