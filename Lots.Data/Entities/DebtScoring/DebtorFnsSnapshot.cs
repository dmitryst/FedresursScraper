using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities.DebtScoring;

[Table("DebtorFnsSnapshots")]
public class DebtorFnsSnapshot
{
    [Key]
    public Guid LotId { get; set; }

    public DebtorEnrichmentProfile EnrichmentProfile { get; set; } = null!;

    public DebtorCompanyStatus CompanyStatus { get; set; } = DebtorCompanyStatus.Unknown;

    [MaxLength(500)]
    public string? CompanyStatusText { get; set; }

    public bool IsStopFactor { get; set; }

    public decimal? Revenue { get; set; }

    public decimal? NetAssets { get; set; }

    [MaxLength(2000)]
    public string? RawResponse { get; set; }

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
