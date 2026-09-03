using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Psikoloji.Application.Common.Interfaces;

namespace Psikoloji.Infrastructure.BackgroundJobs;

/// <summary>
/// Kuyruktan (IBackgroundJobQueue) job ID'lerini sırayla çeker, her biri
/// için yeni bir DI scope'unda ITranscriptionJobRunner'ı çalıştırır. Tek
/// makine, tek worker -- aynı anda sadece bir job işleniyor (whisper zaten
/// tek GPU/CPU instance'ı, paralel işlemenin bir faydası olmazdı).
/// </summary>
public sealed class TranscriptionJobProcessingService : BackgroundService
{
    private readonly IBackgroundJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TranscriptionJobProcessingService> _logger;

    public TranscriptionJobProcessingService(
        IBackgroundJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<TranscriptionJobProcessingService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Transkripsiyon job worker'ı başladı.");

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid jobId;
            try
            {
                jobId = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break; // uygulama kapanıyor
            }

            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<ITranscriptionJobRunner>();

            try
            {
                // TranscriptionJobRunner kendi gerçek iptal token'ını
                // (IJobCancellationRegistry üzerinden) üretiyor -- buraya
                // geçirdiğimiz token sadece uygulama kapanışını haber vermek için.
                await runner.RunAsync(jobId, stoppingToken);
            }
            catch (Exception ex)
            {
                // Runner kendi içinde zaten job.Status'u Failed/Cancelled
                // olarak işaretleyip DB'ye yazıyor -- burada sadece logluyoruz.
                // Worker döngüsü ASLA durmamalı: bir job patlarsa bile
                // sıradaki job işlenmeye devam etmeli.
                _logger.LogError(ex, "Job worker döngüsünde beklenmeyen hata: {JobId}", jobId);
            }
        }

        _logger.LogInformation("Transkripsiyon job worker'ı durdu.");
    }
}
