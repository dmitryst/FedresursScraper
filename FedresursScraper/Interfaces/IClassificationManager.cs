public interface IClassificationManager
{
    Task EnqueueClassificationAsync(Guid lotId, string description, string source);
}