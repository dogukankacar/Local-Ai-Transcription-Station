using Psikoloji.Domain.Entities;

namespace Psikoloji.Application.Interviews.Commands.ProcessVideoInterview;

public sealed record ProcessVideoInterviewResult(
    string SrtFilePath,
    string FullText,
    string FullTextCensored,
    IReadOnlyList<TranscriptSegment> Segments,
    TimeSpan AudioDuration);
