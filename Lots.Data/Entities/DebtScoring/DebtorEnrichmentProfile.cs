using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Lots.Data.Entities;

namespace Lots.Data.Entities.DebtScoring;

/// <summary>
/// Состояние обогащения профиля должника по лоту дебиторки.
/// </summary>
[Table("DebtorEnrichmentProfiles")]
public class DebtorEnrichmentProfile
{
    [Key]
    public Guid LotId { get; set; }

    public DebtLotProfile DebtLotProfile { get; set; } = null!;

    public Guid? SubjectId { get; set; }

    public Subject? Subject { get; set; }

    public SubjectType DebtorType { get; set; }

    /// <summary>
    /// Зашифрованное значение (DataProtection payload может быть &gt; 500 символов).
    /// </summary>
    [MaxLength(2000)]
    public string? ResolvedName { get; set; }

    public bool IsResolvedNameEncrypted { get; set; }

    [MaxLength(2000)]
    public string? ResolvedInn { get; set; }

    public bool IsResolvedInnEncrypted { get; set; }

    [MaxLength(2000)]
    public string? ResolvedSnils { get; set; }

    public bool IsResolvedSnilsEncrypted { get; set; }

    public DebtEnrichmentStepStatus FnsStepStatus { get; set; } = DebtEnrichmentStepStatus.Pending;

    public DebtEnrichmentStepStatus BankruptcyStepStatus { get; set; } = DebtEnrichmentStepStatus.Pending;

    public DebtEnrichmentStepStatus KadStepStatus { get; set; } = DebtEnrichmentStepStatus.Pending;

    public DebtEnrichmentStepStatus FsspStepStatus { get; set; } = DebtEnrichmentStepStatus.Pending;

    public int Attempts { get; set; }

    public DateTime? NextAttemptAt { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DebtorFnsSnapshot? FnsSnapshot { get; set; }

    public DebtorBankruptcyCheck? BankruptcyCheck { get; set; }

    public DebtorKadCaseSnapshot? KadCaseSnapshot { get; set; }

    public ICollection<DebtorFsspRecord> FsspRecords { get; set; } = new List<DebtorFsspRecord>();
}
