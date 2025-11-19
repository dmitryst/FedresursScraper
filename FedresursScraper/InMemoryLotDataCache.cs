using System.Collections.Concurrent;
using FedresursScraper.Services.Models;

// Enum для отслеживания статуса парсинга
public enum ParsingStatus
{
    New,
    Completed
}

public class InMemoryLotDataCache : ILotDataCache
{
    private readonly ILogger<InMemoryLotDataCache> _logger;
    private readonly ConcurrentDictionary<Guid, (LotData data, ParsingStatus status)> _lotCache = new();

    public InMemoryLotDataCache(ILogger<InMemoryLotDataCache> logger)
    {
        _logger = logger;
    }

    public int AddMany(IEnumerable<LotData> newLots)
    {
        int countAdded = 0;
        foreach (var lotData in newLots)
        {
            // TryAdd атомарно добавляет элемент, только если ключа еще нет.
            // Это гарантирует, что мы не перезапишем статус 'Completed' на 'New'.
            if (_lotCache.TryAdd(lotData.Id, (lotData, ParsingStatus.New)))
            {
                countAdded++;
            }
        }
        return countAdded;
    }

    public IReadOnlyCollection<LotData> GetDataToParse()
    {
        // Выбираем только те записи, у которых статус New, и возвращаем сами объекты lotData.
        return _lotCache.Values
            .Where(item => item.status == ParsingStatus.New)
            .Select(item => item.data)
            .ToList()
            .AsReadOnly();
    }

    public void MarkAsCompleted(Guid lotId)
    {
        // Находим текущее значение по ключу
        if (_lotCache.TryGetValue(lotId, out var currentItem))
        {
            // Создаем новое значение с обновленным статусом
            var newItem = (currentItem.data, ParsingStatus.Completed);

            // Атомарно обновляем запись, только если текущее значение совпадает с тем, что мы прочитали.
            _lotCache.TryUpdate(lotId, newItem, currentItem);
        }
    }
    
    public void PruneCompleted()
    {
        // Находим все ключи, которые нужно удалить
        var keysToRemove = _lotCache
            .Where(pair => pair.Value.status == ParsingStatus.Completed)
            .Select(pair => pair.Key)
            .ToList();
        
        if (!keysToRemove.Any())
        {
            return; // Нечего удалять
        }

        // Удаляем их из словаря
        foreach (var key in keysToRemove)
        {
            _lotCache.TryRemove(key, out _);
        }

        _logger.LogInformation("Очищено {Count} обработанных записей из кэша.", keysToRemove.Count);
    }
}
