using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities.DebtScoring;

/// <summary>
/// Карточка лота дебиторской задолженности и состояние пайплайна обработки.
/// </summary>
[Table("DebtLotProfiles")]
public class DebtLotProfile
{
    [Key]
    public Guid LotId { get; set; }

    public Lot Lot { get; set; } = null!;

    public DebtLotProcessingStatus Status { get; set; } = DebtLotProcessingStatus.PendingDocuments;

    /// <summary>
    /// Номинал задолженности (из описания лота или судебного акта).
    /// </summary>
    public decimal? DebtNominal { get; set; }

    /// <summary>
    /// Основание возникновения долга (займ, оспоренная сделка и т.д.).
    /// </summary>
    [MaxLength(500)]
    public string? DebtBasis { get; set; }

    /// <summary>
    /// Номер судебного дела, извлечённый из документов.
    /// </summary>
    [MaxLength(50)]
    public string? CaseNumber { get; set; }

    /// <summary>
    /// Причина отклонения лота (например, номинал ниже порога).
    /// </summary>
    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    public int Attempts { get; set; }

    public DateTime? NextAttemptAt { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<DebtCourtDocument> CourtDocuments { get; set; } = new List<DebtCourtDocument>();

    public ICollection<DebtExtractedEntity> ExtractedEntities { get; set; } = new List<DebtExtractedEntity>();
}
