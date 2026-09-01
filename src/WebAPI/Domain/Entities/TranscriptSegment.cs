namespace Psikoloji.Domain.Entities;

/// <summary>
/// Python AI motorundan dönen tek bir zaman damgalı konuşma segmenti.
/// </summary>
public sealed class TranscriptSegment
{
    public double Start { get; init; }
    public double End { get; init; }

    /// <summary>Ham (sansürsüz) metin. Kalıcı depoya yazılmadan önce erişim kısıtlanmalı.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Kişisel verilerin [GİZLENDİ] ile değiştirildiği güvenli metin.</summary>
    public string TextCensored { get; init; } = string.Empty;
}
