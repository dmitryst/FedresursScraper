using System.Threading.Tasks;

public interface ILotClassifier
{
    Task<LotClassificationResult?> ClassifyLotAsync(string lotDescription, CancellationToken token);
    
    /// <summary>
    /// Классифицирует несколько лотов в одном запросе к API для экономии токенов.
    /// </summary>
    /// <param name="lotDescriptions">Словарь: ID лота -> описание лота</param>
    /// <param name="token">Токен отмены</param>
    /// <returns>Словарь: ID лота -> результат классификации</returns>
    Task<Dictionary<Guid, LotClassificationResult>> ClassifyLotsBatchAsync(
        Dictionary<Guid, string> lotDescriptions, 
        CancellationToken token);
}
