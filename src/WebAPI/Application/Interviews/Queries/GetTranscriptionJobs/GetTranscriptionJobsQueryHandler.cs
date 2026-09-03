using MediatR;
using Microsoft.EntityFrameworkCore;
using Psikoloji.Application.Common.Interfaces;

namespace Psikoloji.Application.Interviews.Queries.GetTranscriptionJobs;

public sealed class GetTranscriptionJobsQueryHandler
    : IRequestHandler<GetTranscriptionJobsQuery, PagedJobsResultDto>
{
    private readonly IApplicationDbContext _db;

    public GetTranscriptionJobsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedJobsResultDto> Handle(
        GetTranscriptionJobsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var totalCount = await _db.TranscriptionJobs.CountAsync(cancellationToken);

        var items = await _db.TranscriptionJobs
            .AsNoTracking()
            .OrderByDescending(j => j.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new TranscriptionJobSummaryDto(
                j.Id,
                j.Status.ToString(),
                j.OriginalFileName,
                j.Diarization,
                j.AudioDurationSeconds,
                j.CreatedAtUtc,
                j.CompletedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedJobsResultDto(items, totalCount, page, pageSize);
    }
}
