using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities.DebtScoring;

[Table("DebtorKadCaseSnapshots")]
public class DebtorKadCaseSnapshot
{
    [Key]
    public Guid LotId { get; set; }

    public DebtorEnrichmentProfile EnrichmentProfile { get; set; } = null!;

    [MaxLength(50)]
    public string? CaseNumber { get; set; }

    [MaxLength(1000)]
    public string? CaseSubject { get; set; }

    [MaxLength(500)]
    public string? DisputeCategory { get; set; }

    [MaxLength(500)]
    public string? CourtName { get; set; }

    public DateTime? LastActDate { get; set; }

    /// <summary>
    /// JSON-список судебных актов (название, дата, ссылка).
    /// </summary>
    public string? DocumentsJson { get; set; }

    [MaxLength(2000)]
    public string? RawResponse { get; set; }

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
