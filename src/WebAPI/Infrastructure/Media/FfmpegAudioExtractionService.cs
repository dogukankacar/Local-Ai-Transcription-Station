using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Psikoloji.Application.Common.Interfaces;
using Psikoloji.Application.Common.Models;

namespace Psikoloji.Infrastructure.Media;

public sealed class FfmpegAudioExtractionService : IAudioExtractionService
{
    private readonly FfmpegOptions _options;
    private readonly ILogger<FfmpegAudioExtractionService> _logger;

    public FfmpegAudioExtractionService(
        IOptions<FfmpegOptions> options,
        ILogger<FfmpegAudioExtractionService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AudioExtractionResult> ExtractAudioAsync(
        string videoFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(videoFilePath))
            throw new FileNotFoundException("Video dosyası bulunamadı.", videoFilePath);

        Directory.CreateDirectory(_options.TempAudioDirectory);
        var outputPath = Path.Combine(_options.TempAudioDirectory, $"{Guid.NewGuid():N}.wav");

        // faster-whisper için önerilen format: 16kHz, mono, 16-bit PCM.
        // -vn: video akışını at, sadece sesi çıkar.
        var arguments = $"-y -i \"{videoFilePath}\" -vn -ac 1 -ar 16000 -acodec pcm_s16le \"{outputPath}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            Arguments = arguments,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _logger.LogInformation("FFmpeg ses çıkarımı başladı: {Video}", videoFilePath);

        using var process = new Process { StartInfo = startInfo };
        var stderrBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stderrBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            var stderr = stderrBuilder.ToString();
            _logger.LogError("FFmpeg başarısız oldu (exit={ExitCode}): {Stderr}", process.ExitCode, stderr);
            throw new InvalidOperationException($"FFmpeg ses çıkarımı başarısız oldu: {stderr}");
        }

        var duration = await GetAudioDurationAsync(outputPath, cancellationToken);
        _logger.LogInformation("Ses dosyası oluşturuldu: {Path} ({Duration})", outputPath, duration);

        return new AudioExtractionResult
        {
            AudioFilePath = outputPath,
            Duration = duration,
        };
    }

    private async Task<TimeSpan> GetAudioDurationAsync(string audioFilePath, CancellationToken cancellationToken)
    {
        // ffprobe, ffmpeg kurulumuyla birlikte gelir; süreyi doğrudan buradan okuyoruz.
        var probeStartInfo = new ProcessStartInfo
        {
            FileName = _options.FfprobeExecutablePath,
            Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{audioFilePath}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var probe = new Process { StartInfo = probeStartInfo };
            probe.Start();
            var output = await probe.StandardOutput.ReadToEndAsync(cancellationToken);
            await probe.WaitForExitAsync(cancellationToken);

            if (double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                return TimeSpan.FromSeconds(seconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ffprobe ile süre okunamadı, süre 0 olarak işaretlenecek.");
        }

        return TimeSpan.Zero;
    }
}
