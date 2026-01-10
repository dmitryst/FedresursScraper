public class PriceScheduleDto
{
    public int Number { get; set; }
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