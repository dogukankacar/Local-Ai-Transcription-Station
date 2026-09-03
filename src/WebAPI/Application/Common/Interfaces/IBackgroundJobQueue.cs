namespace Psikoloji.Application.Common.Interfaces;

public interface IBackgroundJobQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default);

    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}
