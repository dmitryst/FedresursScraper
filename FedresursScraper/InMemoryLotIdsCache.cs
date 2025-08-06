using System.Collections.Concurrent;

public class InMemoryLotIdsCache : ILotIdsCache
{
    private readonly ConcurrentDictionary<string, ParsingStatus> _lotStatuses = new();

    public int AddMany(IEnumerable<string> lotIds)
    {
        int countAdded = 0;
        foreach (var id in lotIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                // TryAdd атомарно добавляет элемент, только если ключа еще нет.
                // Это гарантирует, что мы не перезапишем статус 'Completed' на 'New'.
                if (_lotStatuses.TryAdd(id, ParsingStatus.New))
                {
                    countAdded++;
                }
            }
        }
        return countAdded;
    }

    public IReadOnlyCollection<string> GetIdsToParse()
    {
        // Выбираем только те ID, у которых статус New
        return _lotStatuses
            .Where(pair => pair.Value == ParsingStatus.New)
            .Select(pair => pair.Key)
            .ToList()
            .AsReadOnly();
    }

    public void MarkAsCompleted(string lotId)
    {
        if (!string.IsNullOrWhiteSpace(lotId))
        {
            // Обновляем статус существующего ID на Completed
            _lotStatuses.TryUpdate(lotId, ParsingStatus.Completed, ParsingStatus.New);
        }
    }
}
