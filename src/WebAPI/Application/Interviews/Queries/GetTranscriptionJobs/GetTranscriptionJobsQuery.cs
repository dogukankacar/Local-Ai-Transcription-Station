using MediatR;

namespace Psikoloji.Application.Interviews.Queries.GetTranscriptionJobs;

/// <summary>En yeni önce sıralı, sayfalanmış job listesi.</summary>
public sealed record GetTranscriptionJobsQuery(
    int Page = 1,
    int PageSize = 20) : IRequest<PagedJobsResultDto>;

/// <summary>
/// Geçmiş listesi için hafif bir özet -- tam metinleri (FullText/
/// FullTextCensored) BİLEREK içermiyor, liste ekranında ihtiyaç yok,
/// gereksiz büyük payload olurdu. Detay için GetTranscriptionJobStatusQuery
/// kullanılıyor (kullanıcı bir kayda tıklayınca).
/// </summary>
public sealed record TranscriptionJobSummaryDto(
    Guid Id,
    string Status,
    string? OriginalFileName,
    bool Diarization,
    double? AudioDurationSeconds,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

/// <summary>UI'ın sayfalama kontrollerini (toplam sayfa sayısı vb.)
/// çizebilmesi için toplam kayıt sayısını da taşıyan sarmalayıcı.</summary>
public sealed record PagedJobsResultDto(
    IReadOnlyList<TranscriptionJobSummaryDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
