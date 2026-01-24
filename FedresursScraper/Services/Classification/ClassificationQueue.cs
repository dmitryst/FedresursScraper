using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace FedresursScraper.Services;

public class ClassificationQueue : IClassificationQueue
{
    private readonly ConcurrentQueue<Func<IServiceProvider, CancellationToken, ValueTask>> _workItems = new();
    private readonly SemaphoreSlim _signal = new(0);

    public async ValueTask<Func<IServiceProvider, CancellationToken, ValueTask>?> DequeueAsync(CancellationToken cancellationToken)
    {
        await _signal.WaitAsync(cancellationToken);
        _workItems.TryDequeue(out var workItem);
        return workItem;
    }

    public ValueTask QueueBackgroundWorkItemAsync(Func<IServiceProvider, CancellationToken, ValueTask> workItem)
    {
        if (workItem == null)
        {
            throw new ArgumentNullException(nameof(workItem));
        }

        _workItems.Enqueue(workItem);
        _signal.Release();
        return ValueTask.CompletedTask;
    }

    public int GetQueueSize()
    {
        return _workItems.Count;
    }
}
