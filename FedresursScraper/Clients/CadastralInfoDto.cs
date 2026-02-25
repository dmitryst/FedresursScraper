using System.Text.Json;

public class CadastralInfoDto
{
    public string RawGeoJson { get; set; } = default!;
    
    // Вспомогательные свойства для удобства
    public double? Area { get; set; }
    public decimal? CadastralCost { get; set; }
    public string? Category { get; set; }
    public string? PermittedUse { get; set; }
    public string? Address { get; set; }
}
