namespace Psikoloji.Application.Common.Models;

public sealed class AudioExtractionResult
{
    public string AudioFilePath { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
}
