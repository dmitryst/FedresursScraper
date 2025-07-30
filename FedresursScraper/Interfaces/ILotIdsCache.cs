public interface ILotIdsCache
{
    IReadOnlyCollection<string> GetAllLotIds();
    void ReplaceAll(IEnumerable<string> newIds);
    bool TryAdd(string lotId);
    bool Remove(string lotId);
    int AddMany(IEnumerable<string> lotIds);
}