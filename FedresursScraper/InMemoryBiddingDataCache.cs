using System.Collections.Concurrent;
using FedresursScraper.Services.Models;

// Enum для отслеживания статуса парсинга
public enum ParsingStatus
{
    New,
    Completed
}

public class InMemoryBiddingDataCache : IBiddingDataCache
{
    private readonly ILogger<InMemoryBiddingDataCache> _logger;
    private readonly ConcurrentDictionary<Guid, (BiddingData data, ParsingStatus status)> _biddingCache = new();

    public InMemoryBiddingDataCache(ILogger<InMemoryBiddingDataCache> logger)
    {
        _logger = logger;
    }

    public int AddMany(IEnumerable<BiddingData> newBiddings)
    {
        int countAdded = 0;
        foreach (var biddingData in newBiddings)
        {
            // TryAdd атомарно добавляет элемент, только если ключа еще нет.
            // Это гарантирует, что мы не перезапишем статус 'Completed' на 'New'.
            if (_biddingCache.TryAdd(biddingData.Id, (biddingData, ParsingStatus.New)))
            {
                countAdded++;
            }
        }
        return countAdded;
    }

    public IReadOnlyCollection<BiddingData> GetDataToParse()
    {
        // Выбираем только те записи, у которых статус New, и возвращаем сами объекты BiddingData.
        return _biddingCache.Values
            .Where(item => item.status == ParsingStatus.New)
            .Select(item => item.data)
            .ToList()
            .AsReadOnly();
    }

    public void MarkAsCompleted(Guid biddingId)
    {
        // Находим текущее значение по ключу
        if (_biddingCache.TryGetValue(biddingId, out var currentItem))
        {
            // Создаем новое значение с обновленным статусом
            var newItem = (currentItem.data, ParsingStatus.Completed);

            // Атомарно обновляем запись, только если текущее значение совпадает с тем, что мы прочитали.
            _biddingCache.TryUpdate(biddingId, newItem, currentItem);
        }
    }
    
    public void PruneCompleted()
    {
        // Находим все ключи, которые нужно удалить
        var keysToRemove = _biddingCache
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
            _biddingCache.TryRemove(key, out _);
        }

        _logger.LogInformation("Очищено {Count} обработанных записей из кэша.", keysToRemove.Count);
    }
}
