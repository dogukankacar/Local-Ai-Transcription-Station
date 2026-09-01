using Microsoft.EntityFrameworkCore;
using Psikoloji.Domain.Entities;

namespace Psikoloji.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<TranscriptionJob> TranscriptionJobs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
