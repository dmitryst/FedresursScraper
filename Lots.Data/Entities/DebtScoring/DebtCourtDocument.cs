using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities.DebtScoring;

/// <summary>
/// Судебный документ лота: метаданные обработки и результат OCR.
/// </summary>
[Table("DebtCourtDocuments")]
public class DebtCourtDocument
{
    public Guid Id { get; set; }

    public Guid LotId { get; set; }

    public DebtLotProfile Profile { get; set; } = null!;

    /// <summary>
    /// Связь с общей таблицей документов лота (файл в S3).
    /// </summary>
    public Guid? LotDocumentId { get; set; }

    public LotDocument? LotDocument { get; set; }

    /// <summary>
    /// Исходный URL для скачивания (если документ найден по ссылке в описании).
    /// </summary>
    [MaxLength(2000)]
    public string? SourceUrl { get; set; }

    [MaxLength(500)]
    public string? Title { get; set; }

    [MaxLength(20)]
    public string? Extension { get; set; }

    public CourtDocumentType DocumentType { get; set; } = CourtDocumentType.Unknown;

    public CourtDocumentProcessingStatus ProcessingStatus { get; set; } = CourtDocumentProcessingStatus.Pending;

    /// <summary>
    /// Распознанный текст (из OCR или прямого извлечения из docx).
    /// </summary>
    public string? OcrText { get; set; }

    public double? OcrConfidence { get; set; }

    public int Attempts { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }

    public ICollection<DebtExtractedEntity> ExtractedEntities { get; set; } = new List<DebtExtractedEntity>();
}
