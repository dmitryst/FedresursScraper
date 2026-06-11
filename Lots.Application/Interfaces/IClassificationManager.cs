public interface IClassificationManager
{
    Task EnqueueClassificationAsync(Guid lotId, string description, string source);
    
    /// <summary>
    /// Классифицирует несколько лотов батчем без использования очереди.
    /// Используется для экономии токенов API при обработке множества лотов.
    /// </summary>
    /// <param name="lotIds">Список ID лотов для классификации</param>
    /// <param name="source">Источник запроса классификации</param>
    /// <returns>ID лотов, успешно классифицированных в этом батче.</returns>
    Task<IReadOnlyList<Guid>> ClassifyLotsBatchAsync(List<Guid> lotIds, string source);
}