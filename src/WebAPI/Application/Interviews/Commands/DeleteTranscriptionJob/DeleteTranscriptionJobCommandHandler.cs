using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Psikoloji.Application.Common.Interfaces;
using Psikoloji.Domain.Enums;

namespace Psikoloji.Application.Interviews.Commands.DeleteTranscriptionJob;

public sealed class DeleteTranscriptionJobCommandHandler : IRequestHandler<DeleteTranscriptionJobCommand, bool>
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<DeleteTranscriptionJobCommandHandler> _logger;

    public DeleteTranscriptionJobCommandHandler(
        IApplicationDbContext db, ILogger<DeleteTranscriptionJobCommandHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> Handle(DeleteTranscriptionJobCommand request, CancellationToken cancellationToken)
    {
        var job = await _db.TranscriptionJobs.FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken);
        if (job is null)
            return false;

        // Aktif çalışan bir işi silmeye izin vermiyoruz -- önce iptal
        // edilmeli (kullanıcı zaten "İptal Et" butonunu kullanabilir).
        if (job.Status == JobStatus.Processing)
            return false;

        // KVKK veri minimizasyonu: kayıt silinirken diskte kalan video ve
        // SRT dosyalarını da temizliyoruz -- sadece DB satırını silmek
        // yetmez, gerçek dosyalar diskte kalmaya devam ederdi.
        TryDeleteFile(job.VideoFilePath);
        TryDeleteFile(job.SrtFilePath);

        _db.TranscriptionJobs.Remove(job);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Job silindi: {JobId}", job.Id);
        return true;
    }

    private void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            // Dosya silinemese bile (ör. hâlâ bir process tarafından
            // kilitli) DB kaydının silinmesini engellememeli.
            _logger.LogWarning(ex, "Dosya silinemedi (göz ardı edilebilir): {Path}", path);
        }
    }
}
