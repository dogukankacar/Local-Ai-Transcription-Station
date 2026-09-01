using Psikoloji.Domain.Entities;

namespace Psikoloji.Application.Common.Models;

public sealed class TranscriptionResult
{
    public string Status { get; init; } = string.Empty;
    public string FullText { get; init; } = string.Empty;
    public string FullTextCensored { get; init; } = string.Empty;
    public IReadOnlyList<TranscriptSegment> Segments { get; init; } = Array.Empty<TranscriptSegment>();

    /// <summary>
    /// DİKKAT: Bu liste orijinal (sansürlenmemiş) kişisel veriyi içerir
    /// (ör. gerçek isim/şehir). Sadece anlık doğrulama/denetim amaçlı
    /// kullanılmalı; veritabanına veya log dosyalarına ASLA yazılmamalı.
    /// </summary>
    public IReadOnlyList<DetectedEntity> DetectedEntities { get; init; } = Array.Empty<DetectedEntity>();
}

public sealed class DetectedEntity
{
    public string Text { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int Start { get; init; }
    public int End { get; init; }
}
