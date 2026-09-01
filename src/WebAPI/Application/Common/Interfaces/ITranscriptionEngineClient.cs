using Psikoloji.Application.Common.Models;

namespace Psikoloji.Application.Common.Interfaces;

public interface ITranscriptionEngineClient
{
    /// <summary>
    /// Yerel FastAPI servisine (http://127.0.0.1:8500/transcribe) istek atar.
    /// Büyük dosyalarda bu çağrı uzun sürebilir; çağıran taraf (arka plan
    /// worker'ı) buna göre timeout ayarlamalı ve UI thread'ini bloklamamalı.
    /// </summary>
    Task<TranscriptionResult> TranscribeAsync(
        string audioFilePath,
        string language,
        IReadOnlyCollection<string> censorLabels,
        bool diarization,
        CancellationToken cancellationToken = default);
}
