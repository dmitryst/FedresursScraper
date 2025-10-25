// FedresursScraper/Controllers/Models/MapLotsResponse.cs (новый файл)

using FedresursScraper.Controllers.Models;

public class MapLotsResponse
{
    public IEnumerable<LotGeoDto> Lots { get; set; }
    public bool HasFullAccess { get; set; }
    public int TotalCount { get; set; }
}
