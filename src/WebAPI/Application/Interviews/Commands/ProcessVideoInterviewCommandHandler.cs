using MediatR;
using Microsoft.Extensions.Logging;
using Psikoloji.Application.Common.Interfaces;

namespace Psikoloji.Application.Interviews.Commands.ProcessVideoInterview;

public sealed class ProcessVideoInterviewCommandHandler
    : IRequestHandler<ProcessVideoInterviewCommand, ProcessVideoInterviewResult>
{
    private static readonly string[] DefaultCensorLabels = { "PER", "LOC" };

    private readonly IAudioExtractionService _audioExtractionService;
    private readonly ITranscriptionEngineClient _transcriptionEngineClient;
    private readonly ISubtitleGenerator _subtitleGenerator;
    private readonly ILogger<ProcessVideoInterviewCommandHandler> _logger;

    public ProcessVideoInterviewCommandHandler(
        IAudioExtractionService audioExtractionService,
        ITranscriptionEngineClient transcriptionEngineClient,
        ISubtitleGenerator subtitleGenerator,
        ILogger<ProcessVideoInterviewCommandHandler> logger)
    {
        _audioExtractionService = audioExtractionService;
        _transcriptionEngineClient = transcriptionEngineClient;
        _subtitleGenerator = subtitleGenerator;
        _logger = logger;
    }

    public async Task<ProcessVideoInterviewResult> Handle(
        ProcessVideoInterviewCommand request,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.VideoFilePath))
            throw new FileNotFoundException("Video dosyası bulunamadı.", request.VideoFilePath);

        var censorLabels = request.CensorLabels is { Count: > 0 }
            ? request.CensorLabels
            : DefaultCensorLabels;

        // 1) Video -> Ses (FFmpeg)
        var audioResult = await _audioExtractionService.ExtractAudioAsync(
            request.VideoFilePath, cancellationToken);

        try
        {
            // 2) Ses -> Transkript + Sansür (Python AI motoru)
            var transcription = await _transcriptionEngineClient.TranscribeAsync(
                audioResult.AudioFilePath,
                request.Language,
                censorLabels,
                request.Diarization,
                cancellationToken);

            // 3) Segmentler -> Sansürlü SRT
            var srtOutputPath = request.OutputSrtFilePath
                ?? Path.ChangeExtension(request.VideoFilePath, ".srt");

            await _subtitleGenerator.GenerateSrtFileAsync(
                transcription.Segments,
                srtOutputPath,
                useCensoredText: true,
                cancellationToken);

            _logger.LogInformation(
                "Video işleme tamamlandı: {Video} -> {Srt} ({SegmentCount} segment, diarization={Diarization})",
                request.VideoFilePath, srtOutputPath, transcription.Segments.Count, request.Diarization);

            return new ProcessVideoInterviewResult(
                SrtFilePath: srtOutputPath,
                FullText: transcription.FullText,
                FullTextCensored: transcription.FullTextCensored,
                Segments: transcription.Segments,
                AudioDuration: audioResult.Duration);
        }
        finally
        {
            // 4) Cleanup: Başarı ya da hata fark etmeksizin, FFmpeg'in ürettiği
            // geçici ses dosyası diskten silinmeli. KVKK'nın veri minimizasyonu
            // ilkesi gereği ham ses dosyasının sistemde kalıcı bulunmasına
            // ihtiyaç yok -- sadece SRT ve (varsa) orijinal video saklanır.
            TryDeleteTempAudio(audioResult.AudioFilePath);
        }
    }

    private void TryDeleteTempAudio(string audioFilePath)
    {
        try
        {
            if (File.Exists(audioFilePath))
            {
                File.Delete(audioFilePath);
                _logger.LogInformation("Geçici ses dosyası silindi: {Path}", audioFilePath);
            }
        }
        catch (Exception ex)
        {
            // Silme başarısız olsa bile bu, ana işlemi asla başarısız yapmamalı --
            // sadece uyarı olarak loglanır (ör. dosya hâlâ bir process tarafından kilitli olabilir).
            _logger.LogWarning(ex, "Geçici ses dosyası silinemedi: {Path}", audioFilePath);
        }
    }
}
