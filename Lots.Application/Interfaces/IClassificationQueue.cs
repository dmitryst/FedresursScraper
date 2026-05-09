using System;
using System.Threading;
using System.Threading.Tasks;

public interface IClassificationQueue
{
    ValueTask QueueBackgroundWorkItemAsync(Func<IServiceProvider, CancellationToken, ValueTask> workItem);
    ValueTask<Func<IServiceProvider, CancellationToken, ValueTask>?> DequeueAsync(CancellationToken cancellationToken);
    int GetQueueSize();
}
