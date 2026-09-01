using System.Globalization;
using System.Text;
using Psikoloji.Application.Common.Interfaces;
using Psikoloji.Domain.Entities;

namespace Psikoloji.Infrastructure.Subtitles;

public sealed class SrtSubtitleGenerator : ISubtitleGenerator
{
    public string GenerateSrt(IEnumerable<TranscriptSegment> segments, bool useCensoredText = true)
    {
        var sb = new StringBuilder();
        var index = 1;

        foreach (var segment in segments)
        {
            var text = useCensoredText ? segment.TextCensored : segment.Text;

            sb.AppendLine(index.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine($"{FormatTimestamp(segment.Start)} --> {FormatTimestamp(segment.End)}");
            sb.AppendLine(text);
            sb.AppendLine(); // segmentler arası boş satır SRT formatı gereği zorunlu

            index++;
        }

        return sb.ToString();
    }

    public async Task<string> GenerateSrtFileAsync(
        IEnumerable<TranscriptSegment> segments,
        string outputFilePath,
        bool useCensoredText = true,
        CancellationToken cancellationToken = default)
    {
        var content = GenerateSrt(segments, useCensoredText);

        var directory = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(outputFilePath, content, Encoding.UTF8, cancellationToken);
        return outputFilePath;
    }

    private static string FormatTimestamp(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        // SRT formatı: HH:MM:SS,mmm
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
    }
}
