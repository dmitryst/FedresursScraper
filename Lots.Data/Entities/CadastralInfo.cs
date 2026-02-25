using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities;

/// <summary>
/// Обогащенные данные из Росреестра по конкретному кадастровому номеру
/// </summary>
public class CadastralInfo
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string CadastralNumber { get; set; } = default!;

    /// <summary>
    /// Полный ответ от Росреестра (GeoJSON Feature).
    /// В PostgreSQL/MSSQL лучше хранить как JSON/JSONB для возможности фильтрации в будущем.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? RawGeoJson { get; set; }

    // --- Распарсенные данные для быстрой аналитики ---

    public double? Area { get; set; }
    public decimal? CadastralCost { get; set; }
    public string? Category { get; set; }
    public string? PermittedUse { get; set; }
    public string? Address { get; set; }
    public string? Status { get; set; }

    /// <summary>
    /// Связь с лотом (один лот может иметь несколько кадастровых номеров)
    /// </summary>
    public Guid LotId { get; set; }

    [ForeignKey(nameof(LotId))]
    public Lot Lot { get; set; } = default!;
}
