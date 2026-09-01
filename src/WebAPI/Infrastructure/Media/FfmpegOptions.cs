namespace Psikoloji.Infrastructure.Media;

public sealed class FfmpegOptions
{
    public const string SectionName = "Ffmpeg";

    /// <summary>ffmpeg.exe'nin tam yolu, veya PATH'te ise sadece "ffmpeg".</summary>
    public string ExecutablePath { get; set; } = "ffmpeg";

    /// <summary>ffprobe.exe'nin tam yolu, veya PATH'te ise sadece "ffprobe".
    /// Genelde ffmpeg ile aynı klasörde gelir.</summary>
    public string FfprobeExecutablePath { get; set; } = "ffprobe";

    public string TempAudioDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "psikoloji-audio");
}
