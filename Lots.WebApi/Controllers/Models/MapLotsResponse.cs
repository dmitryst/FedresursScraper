// FedresursScraper/Controllers/Models/MapLotsResponse.cs

using System.Security.Principal;
using FedresursScraper.Controllers.Models;

public class MapLotsResponse
{
    public required IEnumerable<LotGeoDto> Lots { get; set; }
    public bool HasFullAccess { get; set; }
    public int TotalCount { get; set; }
    public AccessLevel AccessLevel { get; set; }
}

public enum AccessLevel
{
    Anonymous,
    Limited,
    Full
}
