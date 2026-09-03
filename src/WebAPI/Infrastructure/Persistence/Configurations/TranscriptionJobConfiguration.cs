using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Psikoloji.Domain.Entities;
using Psikoloji.Domain.Enums;

namespace Psikoloji.Infrastructure.Persistence.Configurations;

public sealed class TranscriptionJobConfiguration : IEntityTypeConfiguration<TranscriptionJob>
{
    public void Configure(EntityTypeBuilder<TranscriptionJob> builder)
    {
        builder.ToTable("transcription_jobs");

        builder.HasKey(j => j.Id);

        builder.Property(j => j.VideoFilePath).IsRequired().HasMaxLength(1024);
        builder.Property(j => j.OriginalFileName).HasMaxLength(260); // Windows MAX_PATH sınırı
        builder.Property(j => j.Language).IsRequired().HasMaxLength(10);
        builder.Property(j => j.CensorLabelsCsv).IsRequired().HasMaxLength(200);

        builder.Property(j => j.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(j => j.SrtFilePath).HasMaxLength(1024);
        builder.Property(j => j.Diarization).IsRequired();
        builder.Property(j => j.FullTextCensored);
        builder.Property(j => j.FullText);
        builder.Property(j => j.ErrorMessage);
        builder.Property(j => j.ProgressMessage).HasMaxLength(200);

        builder.Ignore(j => j.CensorLabels); // hesaplanan alan, DB'de kolon değil

        builder.HasIndex(j => j.Status);
        builder.HasIndex(j => j.CreatedAtUtc);
    }
}
