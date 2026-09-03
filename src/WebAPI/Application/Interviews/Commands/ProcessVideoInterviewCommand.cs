using MediatR;

namespace Psikoloji.Application.Interviews.Commands.ProcessVideoInterview;

/// <summary>
/// Bir laboratuvar kamera kaydını uçtan uca işler: ses çıkarımı -> AI
/// transkripsiyon/sansürleme -> sansürlü SRT üretimi.
/// </summary>
public sealed record ProcessVideoInterviewCommand(
    Guid JobId,
    string VideoFilePath,
    string Language = "tr",
    IReadOnlyCollection<string>? CensorLabels = null,
    bool Diarization = false,
    string? OutputSrtFilePath = null) : IRequest<ProcessVideoInterviewResult>;
