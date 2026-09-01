using Hangfire;
using MediatR;
using Psikoloji.Application.Common.Interfaces;
using Psikoloji.Domain.Entities;

namespace Psikoloji.Application.Interviews.Commands.EnqueueVideoInterviewJob;

public sealed class EnqueueVideoInterviewJobCommandHandler
    : IRequestHandler<EnqueueVideoInterviewJobCommand, Guid>
{
    private static readonly string[] DefaultCensorLabels = { "PER", "LOC" };

    private readonly IApplicationDbContext _db;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public EnqueueVideoInterviewJobCommandHandler(IApplicationDbContext db, IBackgroundJobClient backgroundJobClient)
    {
        _db = db;
        _backgroundJobClient = backgroundJobClient;
    }

    public async Task<Guid> Handle(EnqueueVideoInterviewJobCommand request, CancellationToken cancellationToken)
    {
        var labels = request.CensorLabels is { Count: > 0 } ? request.CensorLabels : DefaultCensorLabels;

        var job = new TranscriptionJob
        {
            VideoFilePath = request.VideoFilePath,
            Language = request.Language,
            CensorLabelsCsv = string.Join(',', labels),
            Diarization = request.Diarization,
        };

        _db.TranscriptionJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);

        // Job DB'ye yazıldıktan SONRA Hangfire'a gönderiliyor -- Hangfire
        // job'ı Postgres'teki kendi tablosuna kalıcı olarak yazar, yani
        // API/PC yeniden başlasa bile bu job kaybolmaz ve otomatik devam eder.
        _backgroundJobClient.Enqueue<ITranscriptionJobRunner>(
            runner => runner.RunAsync(job.Id, CancellationToken.None));

        return job.Id;
    }
}

