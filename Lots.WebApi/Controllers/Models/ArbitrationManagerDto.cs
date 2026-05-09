namespace FedresursScraper.Controllers.Models;

/// <summary>
/// Арбитражный управляющий
/// </summary>
public class ArbitrationManagerDto
{
    /// <summary>
    /// ФИО / Наименование арбитражного управляющего
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// ИНН
    /// </summary>
    public string? Inn { get; set; }

    /// <summary>
    /// СНИЛС (для физ. лиц)
    /// </summary>
    public string? Snils { get; set; }

    /// <summary>
    /// ОГРН (для юр. лиц)
    /// </summary>
    public string? Ogrn { get; set; }
}