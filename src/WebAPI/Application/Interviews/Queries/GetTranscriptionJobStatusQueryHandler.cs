using MediatR;
using Microsoft.EntityFrameworkCore;
using Psikoloji.Application.Common.Interfaces;

namespace Psikoloji.Application.Interviews.Queries.GetTranscriptionJobStatus;

public sealed class GetTranscriptionJobStatusQueryHandler
    : IRequestHandler<GetTranscriptionJobStatusQuery, TranscriptionJobStatusDto?>
{
    private readonly IApplicationDbContext _db;

    public GetTranscriptionJobStatusQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<TranscriptionJobStatusDto?> Handle(
        GetTranscriptionJobStatusQuery request, CancellationToken cancellationToken)
    {
        var job = await _db.TranscriptionJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken);

        if (job is null)
            return null;

        return new TranscriptionJobStatusDto(
            job.Id,
            job.Status.ToString(),
            job.SrtFilePath,
            job.FullText,
            job.FullTextCensored,
            job.ErrorMessage,
            job.AudioDurationSeconds,
            job.CreatedAtUtc,
            job.StartedAtUtc,
            job.CompletedAtUtc);
    }
}
