using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Psikoloji.Application.Common.Interfaces;
using Psikoloji.Domain.Enums;

namespace Psikoloji.Application.Interviews.Commands.CancelTranscriptionJob;

public sealed class CancelTranscriptionJobCommandHandler : IRequestHandler<CancelTranscriptionJobCommand, bool>
{
    private readonly IApplicationDbContext _db;
    private readonly IJobCancellationRegistry _cancellationRegistry;
    private readonly ILogger<CancelTranscriptionJobCommandHandler> _logger;

    public CancelTranscriptionJobCommandHandler(
        IApplicationDbContext db,
        IJobCancellationRegistry cancellationRegistry,
        ILogger<CancelTranscriptionJobCommandHandler> logger)
    {
        _db = db;
        _cancellationRegistry = cancellationRegistry;
        _logger = logger;
    }

    public async Task<bool> Handle(CancelTranscriptionJobCommand request, CancellationToken cancellationToken)
    {
        var job = await _db.TranscriptionJobs.FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken);
        if (job is null)
            return false;

        switch (job.Status)
        {
            case JobStatus.Completed:
            case JobStatus.Failed:
            case JobStatus.Cancelled:
                // Zaten bitmiş bir işi iptal etmenin anlamı yok.
                return false;

            case JobStatus.Pending:
                // Hangfire henüz işi almamış olabilir -- doğrudan iptal
                // olarak işaretliyoruz. Runner başlarken bu durumu tekrar
                // kontrol edip erken çıkacak (aşağıdaki güvenlik ağı).
                job.Status = JobStatus.Cancelled;
                job.CompletedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Job iptal edildi (henüz başlamamıştı): {JobId}", job.Id);
                return true;

            case JobStatus.Processing:
                // Aktif çalışan job'a gerçek zamanlı iptal sinyali gönder.
                var signaled = _cancellationRegistry.Cancel(job.Id);
                _logger.LogInformation(
                    "Job için iptal sinyali gönderildi: {JobId} (kayıtlı process bulundu: {Signaled})",
                    job.Id, signaled);
                return true;

            default:
                return false;
        }
    }
}
