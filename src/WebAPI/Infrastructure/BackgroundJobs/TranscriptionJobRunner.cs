using Hangfire;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Psikoloji.Application.Common.Interfaces;
using Psikoloji.Application.Interviews.Commands.ProcessVideoInterview;
using Psikoloji.Domain.Enums;

namespace Psikoloji.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire tarafından çağrılan job runner. Scoped kayıtlı -- Hangfire,
/// her job çalıştırmasında ASP.NET Core DI'dan otomatik yeni bir scope
/// açar (AddHangfire ile birlikte gelen davranış), bu yüzden burada elle
/// IServiceScopeFactory ile uğraşmaya gerek yok (önceki BackgroundService
/// tabanlı çözümde olduğu gibi).
///
/// AutomaticRetry KAPALI (Attempts = 0): Şu an parçalı işleme (chunking)
/// var ama İLERLEME KAYDI (checkpoint/resume) YOK -- yani bir hata
/// olduğunda otomatik retry, işi kaldığı parçadan değil BAŞTAN başlatır.
/// Uzun (1-2 saatlik) bir kayıtta bu, "sonsuz döngü" gibi hissettiren ama
/// aslında her denemede saatler kaybettiren bir davranışa yol açıyordu.
/// Checkpoint/resume eklenene kadar retry'ı kapalı tutup, hatanın
/// dashboard'da net bir "Failed" olarak görünmesini ve kullanıcının BİLEREK
/// yeniden denemesini tercih ediyoruz.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public sealed class TranscriptionJobRunner : ITranscriptionJobRunner
{
    private readonly IApplicationDbContext _db;
    private readonly ISender _mediator;
    private readonly ILogger<TranscriptionJobRunner> _logger;

    public TranscriptionJobRunner(IApplicationDbContext db, ISender mediator, ILogger<TranscriptionJobRunner> logger)
    {
        _db = db;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _db.TranscriptionJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            _logger.LogWarning("Hangfire job DB'de bulunamadı: {JobId}", jobId);
            return;
        }

        job.Status = JobStatus.Processing;
        job.StartedAtUtc = DateTime.UtcNow;
        job.ErrorMessage = null; // önceki bir retry'dan kalan hata mesajını temizle
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Job işleniyor (Hangfire): {JobId} ({Video})", job.Id, job.VideoFilePath);

        try
        {
            // FFmpeg -> Python AI motoru (whisper + diarization + NER) -> SRT ->
            // cleanup zaten ProcessVideoInterviewCommandHandler içinde yapılıyor.
            var result = await _mediator.Send(
                new ProcessVideoInterviewCommand(job.VideoFilePath, job.Language, job.CensorLabels, job.Diarization),
                cancellationToken);

            job.Status = JobStatus.Completed;
            job.SrtFilePath = result.SrtFilePath;
            job.FullText = result.FullText;
            job.FullTextCensored = result.FullTextCensored;
            job.AudioDurationSeconds = result.AudioDuration.TotalSeconds;
            job.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Job tamamlandı: {JobId}", job.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job işlenirken hata oluştu: {JobId}", job.Id);
            job.Status = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            // Tekrar fırlatıyoruz ki Hangfire bunu bir "başarısız deneme"
            // olarak görsün -- hem [AutomaticRetry] devreye girsin hem de
            // dashboard'da kırmızı/failed olarak işaretlensin.
            throw;
        }
    }
}
