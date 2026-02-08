public interface IClassificationManager
{
    Task EnqueueClassificationAsync(Guid lotId, string description, string source);
    
    /// <summary>
    /// Классифицирует несколько лотов батчем без использования очереди.
    /// Используется для экономии токенов API при обработке множества лотов.
    /// </summary>
    /// <param name="lotIds">Список ID лотов для классификации</param>
    /// <param name="source">Источник запроса классификации</param>
    Task ClassifyLotsBatchAsync(List<Guid> lotIds, string source);
}