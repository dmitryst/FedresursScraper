using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities.DebtScoring;

[Table("DebtorBankruptcyChecks")]
public class DebtorBankruptcyCheck
{
    [Key]
    public Guid LotId { get; set; }

    public DebtorEnrichmentProfile EnrichmentProfile { get; set; } = null!;

    public bool IsInBankruptcy { get; set; }

    [MaxLength(50)]
    public string? BankruptcyCaseNumber { get; set; }

    [MaxLength(500)]
    public string? StatusText { get; set; }

    public bool IsStopFactor { get; set; }

    [MaxLength(2000)]
    public string? RawResponse { get; set; }

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
