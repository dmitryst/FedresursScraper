using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities.DebtScoring;

[Table("DebtorFsspRecords")]
public class DebtorFsspRecord
{
    public Guid Id { get; set; }

    public Guid LotId { get; set; }

    public DebtorEnrichmentProfile EnrichmentProfile { get; set; } = null!;

    [MaxLength(50)]
    public string? ProceedingNumber { get; set; }

    public decimal? DebtAmount { get; set; }

    public FsspProceedingStatus Status { get; set; } = FsspProceedingStatus.Unknown;

    /// <summary>
    /// Закрыто по ст. 46 ч. 1 п. 3 или 4 ФЗ-229 (нет имущества / не установлено местонахождение).
    /// </summary>
    public bool ClosedUnderArticle46 { get; set; }

    public bool IsStopFactor { get; set; }

    [MaxLength(2000)]
    public string? Details { get; set; }

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
