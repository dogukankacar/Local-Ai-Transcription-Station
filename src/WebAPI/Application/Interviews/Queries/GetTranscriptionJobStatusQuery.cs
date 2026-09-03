using MediatR;

namespace Psikoloji.Application.Interviews.Queries.GetTranscriptionJobStatus;

public sealed record GetTranscriptionJobStatusQuery(Guid JobId) : IRequest<TranscriptionJobStatusDto?>;

public sealed record TranscriptionJobStatusDto(
    Guid Id,
    string Status,
    string? SrtFilePath,
    /// <summary>DİKKAT: sansürsüz orijinal metin -- kişisel veri içerir.</summary>
    string? FullText,
    string? FullTextCensored,
    string? ErrorMessage,
    double? AudioDurationSeconds,
    int? ProgressPercent,
    string? ProgressMessage,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc);