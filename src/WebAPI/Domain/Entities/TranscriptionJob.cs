using Psikoloji.Domain.Enums;

namespace Psikoloji.Domain.Entities;

public sealed class TranscriptionJob
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string VideoFilePath { get; init; } = string.Empty;

    /// <summary>Kullanıcının bilgisayarındaki orijinal dosya adı (ör.
    /// "Ahmet ile görüşme.mp4") -- geçmiş listesinde tanıma kolaylığı için.
    /// Diskte tuttuğumuz dosya adı (GUID) değil, sadece görüntüleme amaçlı.</summary>
    public string? OriginalFileName { get; init; }

    public string Language { get; init; } = "tr";
    public string CensorLabelsCsv { get; init; } = "PER,LOC";

    public JobStatus Status { get; set; } = JobStatus.Pending;

    /// <summary>Konuşmacı ayrımı bu iş için istendi mi. False ise pyannote
    /// hiç çalıştırılmadı, işlem tamamen whisper-only olarak yapıldı.</summary>
    public bool Diarization { get; init; }

    public string? SrtFilePath { get; set; }

    /// <summary>Sansürlü tam metin. Her zaman güvenle paylaşılabilir.</summary>
    public string? FullTextCensored { get; set; }

    /// <summary>
    /// DİKKAT: SANSÜRSÜZ (ham) tam metin -- kişi/yer adları dahil orijinal
    /// içerik. Sadece akademisyenin kendi arşivi/doğrulama amacıyla,
    /// bilerek istenen "Orijinal Metni İndir" özelliği için tutuluyor.
    /// Bu alanı ASLA e-posta, paylaşılan klasör veya sansürsüz halde
    /// üçüncü bir sisteme aktarma -- KVKK/Etik Kurul kapsamı burada biter,
    /// sorumluluk kullanıcıya (araştırmacıya) geçer.
    /// </summary>
    public string? FullText { get; set; }

    public string? ErrorMessage { get; set; }
    public double? AudioDurationSeconds { get; set; }

    /// <summary>0-100 arası, Python'dan gerçek zamanlı gelen ilerleme yüzdesi.
    /// Job Pending/Completed/Failed/Cancelled iken null olabilir.</summary>
    public int? ProgressPercent { get; set; }

    /// <summary>İnsan-okunur kısa ilerleme açıklaması, ör. "Parça 3/7 işleniyor".</summary>
    public string? ProgressMessage { get; set; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public IReadOnlyCollection<string> CensorLabels =>
        CensorLabelsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
