using Lots.Application.Services.DebtScoring.Models;

namespace Lots.Application.Services.DebtScoring;

public interface ICourtActEntityExtractor
{
    CourtActExtractionResult Extract(string text);
}
