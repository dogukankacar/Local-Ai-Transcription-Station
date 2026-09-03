using MediatR;
using Microsoft.EntityFrameworkCore;
using Psikoloji.Application.Common.Interfaces;
using Psikoloji.Domain.Enums;

namespace Psikoloji.Application.Interviews.Commands.UpdateJobProgress;

public sealed class UpdateJobProgressCommandHandler : IRequestHandler<UpdateJobProgressCommand, bool>
{
    private readonly IApplicationDbContext _db;

    public UpdateJobProgressCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<bool> Handle(UpdateJobProgressCommand request, CancellationToken cancellationToken)
    {
        var job = await _db.TranscriptionJobs.FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken);

        // Job bulunamadıysa ya da artık Processing durumunda değilse (ör.
        // kullanıcı bu arada iptal etti) geç kalmış bir ilerleme bildirimini
        // sessizce görmezden geliyoruz -- hataya gerek yok, Python'un bunu
        // bilmesine gerek yok.
        if (job is null || job.Status != JobStatus.Processing)
            return false;

        job.ProgressPercent = Math.Clamp(request.Percent, 0, 100);
        job.ProgressMessage = request.Message;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
