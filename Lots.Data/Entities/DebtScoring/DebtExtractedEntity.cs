using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities.DebtScoring;

/// <summary>
/// Сущность, извлечённая из судебного документа (ФИО, ИНН, номер дела и т.д.).
/// Персональные данные хранятся в зашифрованном виде (<see cref="IsEncrypted"/>).
/// </summary>
[Table("DebtExtractedEntities")]
public class DebtExtractedEntity
{
    public Guid Id { get; set; }

    public Guid LotId { get; set; }

    public DebtLotProfile Profile { get; set; } = null!;

    public Guid? CourtDocumentId { get; set; }

    public DebtCourtDocument? CourtDocument { get; set; }

    public ExtractedEntityType EntityType { get; set; }

    [MaxLength(2000)]
    public string Value { get; set; } = default!;

    public bool IsEncrypted { get; set; }

    /// <summary>
    /// Уверенность извлечения (0..1).
    /// </summary>
    public double Confidence { get; set; }

    public EntityExtractionSource Source { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
