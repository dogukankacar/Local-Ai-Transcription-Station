using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Psikoloji.Application.Common.Interfaces;
using Psikoloji.Application.Interviews.Commands.ProcessVideoInterview;
using Psikoloji.Domain.Enums;

namespace Psikoloji.Infrastructure.BackgroundJobs;

/// <summary>
/// TranscriptionJobProcessingService (BackgroundService) tarafından
/// çağrılan job runner. Scoped kayıtlı -- her job için yeni bir DI scope
/// içinde çalıştırılıyor.
///
/// İPTAL: gerçek zamanlı iptal için IJobCancellationRegistry kullanıyoruz
/// -- kendi token'ımızı üretip mediator'a onu veriyoruz (parametre olarak
/// gelen cancellationToken sadece uygulama kapanışını haber verir).
///
/// Otomatik retry YOK: checkpoint/resume olmadan otomatik retry, işi
/// baştan başlatıp saatler kaybettiriyordu -- bir hata olduğunda job
/// sadece "Failed" olarak işaretlenir, kullanıcı bilerek tekrar dener.
/// </summary>
public sealed class TranscriptionJobRunner : ITranscriptionJobRunner
{
    private readonly IApplicationDbContext _db;
    private readonly ISender _mediator;
    private readonly IJobCancellationRegistry _cancellationRegistry;
    private readonly ILogger<TranscriptionJobRunner> _logger;

    public TranscriptionJobRunner(
        IApplicationDbContext db,
        ISender mediator,
        IJobCancellationRegistry cancellationRegistry,
        ILogger<TranscriptionJobRunner> logger)
    {
        _db = db;
        _mediator = mediator;
        _cancellationRegistry = cancellationRegistry;
        _logger = logger;
    }

    public async Task RunAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _db.TranscriptionJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            _logger.LogWarning("Kuyruktan gelen job DB'de bulunamadı: {JobId}", jobId);
            return;
        }

        // Güvenlik ağı: kullanıcı, job henüz kuyruktan alınmadan (Pending
        // durumdayken) iptal etmiş olabilir -- bu durumda
        // CancelTranscriptionJobCommandHandler durumu zaten Cancelled
        // yapmıştır, burada hiç işlemeye başlamadan çıkıyoruz.
        if (job.Status == JobStatus.Cancelled)
        {
            _logger.LogInformation("Job başlamadan önce iptal edilmiş, atlanıyor: {JobId}", job.Id);
            return;
        }

        job.Status = JobStatus.Processing;
        job.StartedAtUtc = DateTime.UtcNow;
        job.ErrorMessage = null; // önceki bir denemeden kalan hata mesajını temizle
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Job işleniyor: {JobId} ({Video})", job.Id, job.VideoFilePath);

        // Bu job için GERÇEK bir iptal token'ı kaydediyoruz -- kullanıcı
        // "İptal Et" butonuna basınca bu token sinyal alacak.
        var realCancellationToken = _cancellationRegistry.Register(jobId);

        try
        {
            // FFmpeg -> Python AI motoru (whisper + diarization + NER) -> SRT ->
            // cleanup zaten ProcessVideoInterviewCommandHandler içinde yapılıyor.
            var result = await _mediator.Send(
                new ProcessVideoInterviewCommand(job.Id, job.VideoFilePath, job.Language, job.CensorLabels, job.Diarization),
                realCancellationToken);

            job.Status = JobStatus.Completed;
            job.SrtFilePath = result.SrtFilePath;
            job.FullText = result.FullText;
            job.FullTextCensored = result.FullTextCensored;
            job.AudioDurationSeconds = result.AudioDuration.TotalSeconds;
            job.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Job tamamlandı: {JobId}", job.Id);
        }
        catch (OperationCanceledException)
        {
            // Kullanıcı bilerek iptal etti -- bu bir hata değil, Failed
            // olarak değil Cancelled olarak işaretliyoruz.
            _logger.LogInformation("Job kullanıcı tarafından iptal edildi: {JobId}", job.Id);
            job.Status = JobStatus.Cancelled;
            job.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None); // ana token iptal olmuş olabilir, temiz bir token kullan
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job işlenirken hata oluştu: {JobId}", job.Id);
            job.Status = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            _cancellationRegistry.Unregister(jobId);
        }
    }
}
