using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities;

/// <summary>
/// Предрассчитанные похожие активные лоты для отображения на странице архивного лота.
/// </summary>
public class SimilarLot
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// ID лота, для которого найдены похожие (обычно это архивный лот)
    /// </summary>
    public Guid SourceLotId { get; set; }
    
    /// <summary>
    /// ID найденного похожего лота (активный лот)
    /// </summary>
    public Guid TargetLotId { get; set; }
    
    [ForeignKey(nameof(TargetLotId))]
    public Lot TargetLot { get; set; } = null!;

    /// <summary>
    /// Порядок отображения (1 - самый релевантный, 4 - наименее)
    /// </summary>
    public int Rank { get; set; }
    
    /// <summary>
    /// Какой алгоритм нашел этот лот ("Strict", "Geo", "CategoryFallback")
    /// </summary>
    public string? Algorithm { get; set; }

    public DateTime CalculatedAt { get; set; }
}