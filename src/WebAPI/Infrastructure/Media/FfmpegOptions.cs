namespace Psikoloji.Infrastructure.Media;

public sealed class FfmpegOptions
{
    public const string SectionName = "Ffmpeg";

    /// <summary>ffmpeg.exe'nin tam yolu, veya PATH'te ise sadece "ffmpeg".
    /// Varsayılan olarak uygulamanın KENDİ klasöründe bir "ffmpeg\ffmpeg.exe"
    /// arar (taşınabilir dağıtım için -- kimsenin sistem PATH'ine FFmpeg
    /// eklemesine gerek kalmaması amacıyla); bulamazsa sadece "ffmpeg" ile
    /// sistem PATH'ine düşer (geliştirme ortamında FFmpeg zaten PATH'te
    /// kurulu olduğu için orada hâlâ çalışır).</summary>
    public string ExecutablePath { get; set; } = ResolveDefault("ffmpeg.exe", "ffmpeg");

    /// <summary>ffprobe.exe'nin tam yolu, veya PATH'te ise sadece "ffprobe".
    /// Genelde ffmpeg ile aynı klasörde gelir.</summary>
    public string FfprobeExecutablePath { get; set; } = ResolveDefault("ffprobe.exe", "ffprobe");

    public string TempAudioDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "psikoloji-audio");

    private static string ResolveDefault(string bundledFileName, string fallbackCommand)
    {
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg", bundledFileName);
        return File.Exists(bundledPath) ? bundledPath : fallbackCommand;
    }
}
