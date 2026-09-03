using MediatR;

namespace Psikoloji.Application.Interviews.Commands.CancelTranscriptionJob;

/// <summary>true: iptal sinyali gönderildi/işaretlendi. false: job bulunamadı
/// ya da zaten bitmiş bir durumda (Completed/Failed/Cancelled).</summary>
public sealed record CancelTranscriptionJobCommand(Guid JobId) : IRequest<bool>;
