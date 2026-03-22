using System.Text.Json;

namespace FedresursScraper.Clients;

public class CadastralInfoDto
{
    /// <summary>
    /// Кадастровый номер
    /// </summary>
    public string CadastralNumber { get; set; } = default!;

    /// <summary>
    /// Площаль
    /// </summary>
    public double? Area { get; set; }

    /// <summary>
    /// Кадастровая стоимость
    /// </summary>
    public decimal? CadastralCost { get; set; }

    /// <summary>
    /// Категория земель
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Разрешенное использование
    /// </summary>
    public string? PermittedUse { get; set; }

    /// <summary>
    /// Адрес объекта
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Статус объекта
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Тип (Земельный участок, здание...)
    /// </summary>
    public string? ObjectType { get; set; }

    /// <summary>
    /// Вид права (Собственность, Аренда...)
    /// </summary>
    public string? RightType { get; set; }

    /// <summary>
    /// Форма собственности (Частная, Муниципальная...)
    /// </summary>
    public string? OwnershipType { get; set; }

    /// <summary>
    /// Дата постановки на учет
    /// </summary>
    public string? RegDate { get; set; }

    /// <summary>
    /// Полный ответ от Росреестра (GeoJSON Feature)
    /// </summary>
    public string? RawGeoJson { get; set; }
}
