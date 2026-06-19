using FedresursScraper.Services;

namespace Lots.Application.Interfaces;

public interface ILotDescriptionAlignmentService
{
    Task<IReadOnlyList<LotDescriptionAlignmentPreviewDto>> PreviewAsync(
        IReadOnlyList<int> publicIds,
        CancellationToken cancellationToken = default);

    Task<LotDescriptionAlignmentPreviewDto?> ApplyAsync(
        ApplyLotDescriptionAlignmentRequest request,
        CancellationToken cancellationToken = default);
}
