using Microsoft.EntityFrameworkCore;
using Psikoloji.Application.Common.Interfaces;
using Psikoloji.Domain.Entities;

namespace Psikoloji.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext, IApplicationDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<TranscriptionJob> TranscriptionJobs => Set<TranscriptionJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
