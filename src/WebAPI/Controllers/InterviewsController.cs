using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Psikoloji.Application.Interviews.Commands.EnqueueVideoInterviewJob;
using Psikoloji.Application.Interviews.Queries.GetTranscriptionJobStatus;

namespace Psikoloji.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class InterviewsController : ControllerBase
{
    private static readonly string UploadDirectory =
        Path.Combine(Path.GetTempPath(), "psikoloji-uploads");

    private readonly ISender _sender;
    private readonly ILogger<InterviewsController> _logger;

    public InterviewsController(ISender sender, ILogger<InterviewsController> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    /// <summary>
    /// Laboratuvar kamera kaydını yükler, işleme kuyruğa alınır ve HEMEN
    /// bir jobId ile 202 Accepted döner. Gerçek işlem (FFmpeg -> Python ->
    /// SRT) arka planda TranscriptionJobProcessingService tarafından yapılır.
    /// Durumu öğrenmek için GET /api/interviews/jobs/{jobId} kullanılır.
    /// </summary>
    [HttpPost("process")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(4L * 1024 * 1024 * 1024)] // 4GB -- Program.cs'deki FormOptions ile tutarlı olmalı
    public async Task<ActionResult> ProcessVideoInterview(
        IFormFile videoFile,
        [FromForm] string language = "tr",
        [FromForm] bool diarization = false,
        CancellationToken cancellationToken = default)
    {
        if (videoFile is null || videoFile.Length == 0)
            return BadRequest("Video dosyası boş veya eksik.");

        Directory.CreateDirectory(UploadDirectory);
        var extension = Path.GetExtension(videoFile.FileName);
        var savedVideoPath = Path.Combine(UploadDirectory, $"{Guid.NewGuid():N}{extension}");

        await using (var stream = System.IO.File.Create(savedVideoPath))
        {
            await videoFile.CopyToAsync(stream, cancellationToken);
        }

        _logger.LogInformation(
            "Video yüklendi, kuyruğa alınıyor: {OriginalName} -> {SavedPath} (diarization={Diarization})",
            videoFile.FileName, savedVideoPath, diarization);

        var jobId = await _sender.Send(
            new EnqueueVideoInterviewJobCommand(savedVideoPath, language, Diarization: diarization),
            cancellationToken);

        return AcceptedAtAction(nameof(GetJobStatus), new { id = jobId }, new { jobId });
    }

    /// <summary>Job'un anlık durumunu döner: Pending / Processing / Completed / Failed.</summary>
    [HttpGet("jobs/{id:guid}")]
    public async Task<ActionResult<TranscriptionJobStatusDto>> GetJobStatus(
        Guid id, CancellationToken cancellationToken)
    {
        var status = await _sender.Send(new GetTranscriptionJobStatusQuery(id), cancellationToken);
        return status is null ? NotFound() : Ok(status);
    }

    /// <summary>
    /// Tamamlanmış bir job'un ürettiği .srt dosyasını ham içerik olarak döner.
    /// UI bunu hem önizleme için fetch edebilir hem de doğrudan indirme linki
    /// olarak kullanabilir (href + download attribute).
    /// </summary>
    [HttpGet("jobs/{id:guid}/srt")]
    public async Task<IActionResult> GetJobSrt(Guid id, CancellationToken cancellationToken)
    {
        var status = await _sender.Send(new GetTranscriptionJobStatusQuery(id), cancellationToken);
        if (status is null || string.IsNullOrEmpty(status.SrtFilePath))
            return NotFound("Bu job için henüz üretilmiş bir SRT dosyası yok.");

        if (!System.IO.File.Exists(status.SrtFilePath))
            return NotFound("SRT dosyası diskte bulunamadı (silinmiş olabilir).");

        var bytes = await System.IO.File.ReadAllBytesAsync(status.SrtFilePath, cancellationToken);
        var fileName = Path.GetFileName(status.SrtFilePath);
        return File(bytes, "text/plain; charset=utf-8", fileName);
    }
}

