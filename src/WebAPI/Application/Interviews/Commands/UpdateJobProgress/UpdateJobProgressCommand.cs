using MediatR;

namespace Psikoloji.Application.Interviews.Commands.UpdateJobProgress;

/// <summary>Python AI motorundan gelen gerçek zamanlı ilerleme bildirimi.</summary>
public sealed record UpdateJobProgressCommand(Guid JobId, int Percent, string? Message) : IRequest<bool>;
