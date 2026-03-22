using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities;

/// <summary>
/// Обогащенные данные из Росреестра по конкретному кадастровому номеру
/// </summary>
public class CadastralInfo
{
    /// <summary>
    /// Уникальный идентификатор записи в базе данных
    /// </summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Кадастровый номер объекта (например, "39:10:480001:58")
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string CadastralNumber { get; set; } = default!;

    /// <summary>
    /// Полный ответ от Росреестра (GeoJSON Feature).
    /// В PostgreSQL/MSSQL хранится как JSON/JSONB для возможности поиска и фильтрации.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? RawGeoJson { get; set; }

    // --- Распарсенные данные для быстрой аналитики ---

    /// <summary>
    /// Площадь объекта (в квадратных метрах)
    /// </summary>
    public double? Area { get; set; }

    /// <summary>
    /// Кадастровая стоимость объекта (в рублях)
    /// </summary>
    public decimal? CadastralCost { get; set; }

    /// <summary>
    /// Категория земель (например, "Земли населенных пунктов", "Земли сельскохозяйственного назначения")
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Разрешенное использование (например, "под строительство индивидуального жилого дома", "для ведения садоводства")
    /// </summary>
    public string? PermittedUse { get; set; }

    /// <summary>
    /// Полный адрес объекта по данным ЕГРН
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Статус объекта в ЕГРН (например, "Учтенный", "Ранее учтенный")
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Тип объекта недвижимости (например, "Земельный участок", "Здание", "Помещение")
    /// </summary>
    public string? ObjectType { get; set; }

    /// <summary>
    /// Вид права (например, "Собственность", "Аренда")
    /// </summary>
    public string? RightType { get; set; }

    /// <summary>
    /// Форма собственности (например, "Частная", "Муниципальная", "Федеральная")
    /// </summary>
    public string? OwnershipType { get; set; }

    /// <summary>
    /// Дата постановки объекта на кадастровый учет (в формате YYYY-MM-DD, например "2009-06-19")
    /// </summary>
    public string? RegDate { get; set; }

    /// <summary>
    /// Идентификатор лота, к которому привязан данный кадастровый номер
    /// </summary>
    public Guid LotId { get; set; }

    /// <summary>
    /// Навигационное свойство: Лот, содержащий данный объект
    /// </summary>
    [ForeignKey(nameof(LotId))]
    public Lot Lot { get; set; } = default!;
}