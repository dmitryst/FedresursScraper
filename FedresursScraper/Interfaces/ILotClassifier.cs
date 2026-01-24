using System.Threading.Tasks;

public interface ILotClassifier
{
    Task<LotClassificationResult?> ClassifyLotAsync(string lotDescription, CancellationToken token);
}
