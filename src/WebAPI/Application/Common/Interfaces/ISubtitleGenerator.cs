using Psikoloji.Domain.Entities;

namespace Psikoloji.Application.Common.Interfaces;

public interface ISubtitleGenerator
{
    /// <summary>
    /// Segmentlerden SRT formatında altyazı içeriği üretir.
    /// useCensoredText=true olduğunda kişisel veriler [GİZLENDİ] olarak kalır.
    /// Dışarı paylaşılacak / arşivlenecek altyazılarda bunun true olması
    /// KVKK/Etik Kurul gereği zorunlu kabul edilmelidir.
    /// </summary>
    string GenerateSrt(IEnumerable<TranscriptSegment> segments, bool useCensoredText = true);

    Task<string> GenerateSrtFileAsync(
        IEnumerable<TranscriptSegment> segments,
        string outputFilePath,
        bool useCensoredText = true,
        CancellationToken cancellationToken = default);
}
