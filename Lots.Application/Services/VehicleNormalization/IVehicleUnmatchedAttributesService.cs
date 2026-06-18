namespace Lots.Application.Services.VehicleNormalization;

public record UnmatchedBrandEntry(string Brand, int Count);

public record UnmatchedModelEntry(string Brand, string Model, int Count);

public interface IVehicleUnmatchedAttributesService
{
    Task<IReadOnlyList<UnmatchedBrandEntry>> GetUnmatchedBrandsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UnmatchedModelEntry>> GetUnmatchedModelsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сбрасывает флаги нормализации, чтобы worker прогнал лоты заново (после обновления справочника).
    /// </summary>
    Task<int> ResetNormalizationFlagsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Сбрасывает атрибуты DeepSeek у неразобранных лотов, чтобы FedresursScraper прогнал их заново.
    /// </summary>
    Task<int> ResetUnmatchedExtractionFlagsAsync(CancellationToken cancellationToken = default);
}
