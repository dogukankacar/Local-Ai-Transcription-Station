using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Psikoloji.Application.Interviews.Commands.CancelTranscriptionJob;
using Psikoloji.Application.Interviews.Commands.DeleteTranscriptionJob;
using Psikoloji.Application.Interviews.Commands.EnqueueVideoInterviewJob;
using Psikoloji.Application.Interviews.Commands.UpdateJobProgress;
using Psikoloji.Application.Interviews.Queries.GetTranscriptionJobs;
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
            new EnqueueVideoInterviewJobCommand(
                savedVideoPath, videoFile.FileName, language, Diarization: diarization),
            cancellationToken);

        return AcceptedAtAction(nameof(GetJobStatus), new { id = jobId }, new { jobId });
    }

    /// <summary>Geçmişteki job'ları sayfalanmış şekilde, en yeniden eskiye listeler.</summary>
    [HttpGet("jobs")]
    public async Task<ActionResult<PagedJobsResultDto>> GetJobs(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetTranscriptionJobsQuery(page, pageSize), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Bir job'ı ve ona ait video/SRT dosyalarını kalıcı olarak siler.
    /// Processing durumundaki bir job silinemez -- önce iptal edilmeli.
    /// </summary>
    [HttpDelete("jobs/{id:guid}")]
    public async Task<IActionResult> DeleteJob(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _sender.Send(new DeleteTranscriptionJobCommand(id), cancellationToken);
        return deleted ? Ok(new { deleted = true }) : Conflict(new { deleted = false });
    }

    /// <summary>Job'un anlık durumunu döner: Pending / Processing / Completed / Failed / Cancelled.</summary>
    [HttpGet("jobs/{id:guid}")]
    public async Task<ActionResult<TranscriptionJobStatusDto>> GetJobStatus(
        Guid id, CancellationToken cancellationToken)
    {
        var status = await _sender.Send(new GetTranscriptionJobStatusQuery(id), cancellationToken);
        return status is null ? NotFound() : Ok(status);
    }

    /// <summary>
    /// Devam eden (Pending ya da Processing) bir job'ı iptal eder. Pending
    /// ise hemen Cancelled işaretlenir. Processing ise gerçek zamanlı bir
    /// iptal sinyali gönderilir -- C# tarafı beklemeyi hemen bırakır, ama
    /// Python tarafındaki o an süren GPU/CPU hesaplaması Python'un kendi
    /// doğası gereği anında kesilemez, arka planda sessizce bitene kadar
    /// devam edebilir (bilinen bir sınır).
    /// </summary>
    [HttpPost("jobs/{id:guid}/cancel")]
    public async Task<IActionResult> CancelJob(Guid id, CancellationToken cancellationToken)
    {
        var cancelled = await _sender.Send(new CancelTranscriptionJobCommand(id), cancellationToken);
        return cancelled ? Ok(new { cancelled = true }) : Conflict(new { cancelled = false });
    }

    /// <summary>
    /// SADECE Python AI motoru tarafından çağrılır (localhost içi) --
    /// whisper parça parça ilerledikçe gerçek zamanlı ilerleme bildirir.
    /// UI'ın 3 saniyelik polling'i bu sayede gerçek bir yüzde gösterebiliyor.
    /// </summary>
    [HttpPost("jobs/{id:guid}/progress")]
    public async Task<IActionResult> ReportProgress(
        Guid id, [FromBody] ReportProgressRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new UpdateJobProgressCommand(id, request.Percent, request.Message), cancellationToken);
        return Ok();
    }

    /// <summary>
    /// Tamamlanmış bir job'un ürettiği .srt dosyasını ham içerik olarak döner.</summary>
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

public sealed record ReportProgressRequest(int Percent, string? Message);

