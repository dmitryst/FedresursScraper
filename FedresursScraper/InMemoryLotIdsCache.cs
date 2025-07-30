public class InMemoryLotIdsCache : ILotIdsCache
{
    private readonly HashSet<string> _lotIds = new();
    private readonly object _lock = new();

    public IReadOnlyCollection<string> GetAllLotIds()
    {
        lock (_lock)
        {
            return _lotIds.ToList().AsReadOnly();
        }
    }

    public void ReplaceAll(IEnumerable<string> newIds)
    {
        lock (_lock)
        {
            _lotIds.Clear();
            foreach (var id in newIds)
                _lotIds.Add(id);
        }
    }

    public bool TryAdd(string lotId)
    {
        if (string.IsNullOrWhiteSpace(lotId)) return false;
        lock (_lock)
        {
            return _lotIds.Add(lotId); // вернёт true, если добавлен, false если уже есть
        }
    }

    public bool Remove(string lotId)
    {
        if (string.IsNullOrWhiteSpace(lotId)) return false;
        lock (_lock)
        {
            return _lotIds.Remove(lotId);
        }
    }

    public int AddMany(IEnumerable<string> lotIds)
    {
        int countAdded = 0;
        lock (_lock)
        {
            foreach (var id in lotIds)
            {
                if (!string.IsNullOrWhiteSpace(id) && _lotIds.Add(id))
                    countAdded++;
            }
        }
        return countAdded;
    }
}
