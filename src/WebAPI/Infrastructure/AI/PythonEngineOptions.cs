namespace Psikoloji.Infrastructure.AI;

public sealed class PythonEngineOptions
{
    public const string SectionName = "PythonEngine";

    public string BaseUrl { get; set; } = "http://127.0.0.1:8500";

    /// <summary>Uzun ses/video dosyaları için cömert bir timeout gerekir.
    /// Parçalı işleme (10dk/parça) + CPU'da diarization ile 2 saatlik bir
    /// kayıt toplamda 30 dakikayı kolayca aşabiliyor, bu yüzden varsayılanı
    /// 3 saate çıkardık.</summary>
    public int TimeoutMinutes { get; set; } = 180;
}
