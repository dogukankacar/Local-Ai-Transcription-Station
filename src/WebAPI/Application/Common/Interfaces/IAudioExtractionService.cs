using Psikoloji.Application.Common.Models;

namespace Psikoloji.Application.Common.Interfaces;

public interface IAudioExtractionService
{
    /// <summary>
    /// Verilen video dosyasından (mp4 vb.) sesi çıkarıp geçici bir .wav dosyası
    /// olarak kaydeder. Çıktı, faster-whisper için önerilen 16kHz mono PCM
    /// formatındadır.
    /// </summary>
    Task<AudioExtractionResult> ExtractAudioAsync(
        string videoFilePath,
        CancellationToken cancellationToken = default);
}
