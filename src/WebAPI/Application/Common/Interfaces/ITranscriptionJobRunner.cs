namespace Psikoloji.Application.Common.Interfaces;

/// <summary>
/// Hangfire (ya da başka bir job runner), bu arayüz üzerinden tek bir
/// transkripsiyon job'ını çalıştırır. Application katmanı Hangfire'ı hiç
/// bilmiyor -- sadece "bir job'ı ID'siyle çalıştır" sözleşmesini biliyor.
/// </summary>
public interface ITranscriptionJobRunner
{
    Task RunAsync(Guid jobId, CancellationToken cancellationToken);
}
