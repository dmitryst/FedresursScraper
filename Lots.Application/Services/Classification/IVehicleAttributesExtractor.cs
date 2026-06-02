using System.Threading;
using System.Threading.Tasks;

namespace FedresursScraper.Services;

public interface IVehicleAttributesExtractor
{
    Task ExtractAttributesForActiveVehiclesAsync(CancellationToken token);
}
