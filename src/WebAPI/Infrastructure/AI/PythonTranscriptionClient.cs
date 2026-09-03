using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Psikoloji.Application.Common.Interfaces;
using Psikoloji.Application.Common.Models;
using Psikoloji.Domain.Entities;

namespace Psikoloji.Infrastructure.AI;

public sealed class PythonTranscriptionClient : ITranscriptionEngineClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PythonTranscriptionClient> _logger;

    public PythonTranscriptionClient(HttpClient httpClient, ILogger<PythonTranscriptionClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        Guid jobId,
        string audioFilePath,
        string language,
        IReadOnlyCollection<string> censorLabels,
        bool diarization,
        CancellationToken cancellationToken = default)
    {
        var requestBody = new TranscribeRequestDto
        {
            JobId = jobId.ToString(),
            AudioPath = audioFilePath,
            Language = language,
            CensorLabels = censorLabels.ToList(),
            Diarization = diarization,
        };

        _logger.LogInformation("AI motoruna transkripsiyon isteği gönderiliyor: {Path}", audioFilePath);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("/transcribe", requestBody, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "AI motoruna bağlanılamadı ({BaseAddress}). Python servisi çalışıyor mu?", _httpClient.BaseAddress);
            throw new InvalidOperationException("Yerel AI motoruna (Python/FastAPI) ulaşılamadı.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("AI motoru hata döndürdü ({StatusCode}): {Body}", response.StatusCode, errorBody);
            throw new InvalidOperationException($"AI motoru hata döndürdü: {response.StatusCode} - {errorBody}");
        }

        var dto = await response.Content.ReadFromJsonAsync<TranscribeResponseDto>(cancellationToken: cancellationToken)
                   ?? throw new InvalidOperationException("AI motorundan boş yanıt alındı.");

        return MapToResult(dto);
    }

    private static TranscriptionResult MapToResult(TranscribeResponseDto dto) => new()
    {
        Status = dto.Status,
        FullText = dto.FullText,
        FullTextCensored = dto.FullTextCensored,
        Segments = dto.Segments.Select(s => new TranscriptSegment
        {
            Start = s.Start,
            End = s.End,
            Text = s.Text,
            TextCensored = s.TextCensored,
        }).ToList(),
        DetectedEntities = dto.DetectedEntities.Select(e => new DetectedEntity
        {
            Text = e.Text,
            Label = e.Label,
            Start = e.Start,
            End = e.End,
        }).ToList(),
    };

    // --- Python servisinin JSON şemasıyla birebir eşleşen DTO'lar (snake_case) ---

    private sealed class TranscribeRequestDto
    {
        [JsonPropertyName("job_id")]
        public string JobId { get; init; } = string.Empty;

        [JsonPropertyName("audio_path")]
        public string AudioPath { get; init; } = string.Empty;

        [JsonPropertyName("language")]
        public string Language { get; init; } = "tr";

        [JsonPropertyName("censor_labels")]
        public List<string> CensorLabels { get; init; } = new();

        [JsonPropertyName("diarization")]
        public bool Diarization { get; init; } = true;
    }

    private sealed class TranscribeResponseDto
    {
        [JsonPropertyName("status")]
        public string Status { get; init; } = string.Empty;

        [JsonPropertyName("full_text")]
        public string FullText { get; init; } = string.Empty;

        [JsonPropertyName("full_text_censored")]
        public string FullTextCensored { get; init; } = string.Empty;

        [JsonPropertyName("segments")]
        public List<SegmentDto> Segments { get; init; } = new();

        [JsonPropertyName("detected_entities")]
        public List<EntityDto> DetectedEntities { get; init; } = new();
    }

    private sealed class SegmentDto
    {
        [JsonPropertyName("start")]
        public double Start { get; init; }

        [JsonPropertyName("end")]
        public double End { get; init; }

        [JsonPropertyName("text")]
        public string Text { get; init; } = string.Empty;

        [JsonPropertyName("text_censored")]
        public string TextCensored { get; init; } = string.Empty;
    }

    private sealed class EntityDto
    {
        [JsonPropertyName("text")]
        public string Text { get; init; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; init; } = string.Empty;

        [JsonPropertyName("start")]
        public int Start { get; init; }

        [JsonPropertyName("end")]
        public int End { get; init; }
    }
}
