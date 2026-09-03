using MediatR;
using Psikoloji.Application.Common.Interfaces;
using Psikoloji.Domain.Entities;

namespace Psikoloji.Application.Interviews.Commands.EnqueueVideoInterviewJob;

public sealed class EnqueueVideoInterviewJobCommandHandler
    : IRequestHandler<EnqueueVideoInterviewJobCommand, Guid>
{
    private static readonly string[] DefaultCensorLabels = { "PER", "LOC" };

    private readonly IApplicationDbContext _db;
    private readonly IBackgroundJobQueue _queue;

    public EnqueueVideoInterviewJobCommandHandler(IApplicationDbContext db, IBackgroundJobQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    public async Task<Guid> Handle(EnqueueVideoInterviewJobCommand request, CancellationToken cancellationToken)
    {
        var labels = request.CensorLabels is { Count: > 0 } ? request.CensorLabels : DefaultCensorLabels;

        var job = new TranscriptionJob
        {
            VideoFilePath = request.VideoFilePath,
            OriginalFileName = request.OriginalFileName,
            Language = request.Language,
            CensorLabelsCsv = string.Join(',', labels),
            Diarization = request.Diarization,
        };

        _db.TranscriptionJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);

        // Job DB'ye yazıldıktan SONRA kuyruğa ekleniyor -- worker jobId'yi
        // aldığında kaydın veritabanında zaten mevcut olduğundan emin olmak için.
        await _queue.EnqueueAsync(job.Id, cancellationToken);

        return job.Id;
    }
}
