using MediatR;

namespace Psikoloji.Application.Interviews.Commands.DeleteTranscriptionJob;

/// <summary>true: silindi. false: bulunamadı ya da hâlâ Processing durumunda
/// (önce iptal edilmeli).</summary>
public sealed record DeleteTranscriptionJobCommand(Guid JobId) : IRequest<bool>;
