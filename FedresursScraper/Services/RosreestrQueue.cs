using System.Collections.Concurrent;

public class RosreestrQueue : IRosreestrQueue
{
    private readonly ConcurrentQueue<Func<IServiceProvider, CancellationToken, ValueTask>> _workItems = new();
    private readonly SemaphoreSlim _signal = new(0);

    public ValueTask QueueWorkItemAsync(Func<IServiceProvider, CancellationToken, ValueTask> workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        _workItems.Enqueue(workItem);
        _signal.Release();
        return ValueTask.CompletedTask;
    }

    public async ValueTask<Func<IServiceProvider, CancellationToken, ValueTask>> DequeueAsync(CancellationToken cancellationToken)
    {
        await _signal.WaitAsync(cancellationToken);
        _workItems.TryDequeue(out var workItem);
        return workItem!;
    }
}
