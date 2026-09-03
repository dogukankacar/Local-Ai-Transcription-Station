using MediatR;

namespace Psikoloji.Application.Interviews.Commands.EnqueueVideoInterviewJob;

public sealed record EnqueueVideoInterviewJobCommand(
    string VideoFilePath,
    string? OriginalFileName = null,
    string Language = "tr",
    IReadOnlyCollection<string>? CensorLabels = null,
    bool Diarization = false) : IRequest<Guid>;
