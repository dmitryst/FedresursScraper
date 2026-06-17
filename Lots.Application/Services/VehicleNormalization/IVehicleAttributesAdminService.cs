namespace Lots.Application.Services.VehicleNormalization;

public record UnmatchedVehicleLotDto(
    Guid Id,
    int PublicId,
    string? LotNumber,
    string? Title,
    string Url,
    string? TradeNumber,
    string? Platform,
    decimal? StartPrice,
    string? Brand,
    string? Model,
    string? BrandRaw,
    string? ModelRaw,
    bool? BrandMatched,
    bool? ModelMatched);

public class UpdateLotVehicleAttributesRequest
{
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public bool RemoveBrand { get; set; }
    public bool RemoveModel { get; set; }
}

public interface IVehicleAttributesAdminService
{
    Task<(IReadOnlyList<UnmatchedVehicleLotDto> Items, int TotalCount)> GetUnmatchedLotsAsync(
        int page,
        int pageSize,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    Task<int> GetUnmatchedLotsCountAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    Task<UnmatchedVehicleLotDto?> UpdateLotVehicleAttributesAsync(
        int publicId,
        UpdateLotVehicleAttributesRequest request,
        CancellationToken cancellationToken = default);
}
