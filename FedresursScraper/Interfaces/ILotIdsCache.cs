public interface ILotIdsCache
{
    /// <summary>
    /// Получает все ID, которые нужно распарсить (со статусом New)
    /// </summary>
    /// <returns></returns>
    IReadOnlyCollection<string> GetIdsToParse();

    /// <summary>
    /// Отмечает ID как успешно обработанный
    /// </summary>
    /// <param name="lotId"></param>
    void MarkAsCompleted(string lotId);
    
    /// <summary>
    /// Добавляет новые ID в кэш (только если их там еще нет)
    /// </summary>
    /// <param name="lotIds"></param>
    /// <returns></returns>
    int AddMany(IEnumerable<string> lotIds);
}
