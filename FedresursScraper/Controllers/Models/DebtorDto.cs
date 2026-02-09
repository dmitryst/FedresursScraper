namespace FedresursScraper.Controllers.Models;

/// <summary>
/// Должник
/// </summary>
public class DebtorDto
{
    /// <summary>
    /// ФИО / Наименование должника
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
