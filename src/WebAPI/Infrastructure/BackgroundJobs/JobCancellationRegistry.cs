using System.Collections.Concurrent;
using Psikoloji.Application.Common.Interfaces;

namespace Psikoloji.Infrastructure.BackgroundJobs;

public sealed class JobCancellationRegistry : IJobCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _tokens = new();

    public CancellationToken Register(Guid jobId)
    {
        var cts = new CancellationTokenSource();
        _tokens[jobId] = cts;
        return cts.Token;
    }

    public bool Cancel(Guid jobId)
    {
        if (_tokens.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
            return true;
        }
        return false;
    }

    public void Unregister(Guid jobId)
    {
        if (_tokens.TryRemove(jobId, out var cts))
        {
            cts.Dispose();
        }
    }
}
