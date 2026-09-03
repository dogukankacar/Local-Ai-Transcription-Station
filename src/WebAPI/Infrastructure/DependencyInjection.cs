using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Psikoloji.Application.Common.Interfaces;
using Psikoloji.Infrastructure.AI;
using Psikoloji.Infrastructure.BackgroundJobs;
using Psikoloji.Infrastructure.Media;
using Psikoloji.Infrastructure.Persistence;
using Psikoloji.Infrastructure.Subtitles;

namespace Psikoloji.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FfmpegOptions>(configuration.GetSection(FfmpegOptions.SectionName));
        services.Configure<PythonEngineOptions>(configuration.GetSection(PythonEngineOptions.SectionName));

        services.AddSingleton<IAudioExtractionService, FfmpegAudioExtractionService>();
        services.AddSingleton<ISubtitleGenerator, SrtSubtitleGenerator>();

        services.AddHttpClient<ITranscriptionEngineClient, PythonTranscriptionClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<PythonEngineOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromMinutes(options.TimeoutMinutes);
        });

        // --- Kalıcılık: SQLite -- tek bir .db dosyası, Docker/ayrı sunucu
        // GEREKTİRMİYOR. Taşınabilir dağıtım için bilinçli tercih: akademisyenin
        // bilgisayarına Docker Desktop kurdurmaya gerek kalmıyor, .db dosyası
        // program klasörünün içinde duruyor.
        var connectionString = configuration.GetConnectionString("Default");
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // --- Arka plan iş kuyruğu: bellek-içi Channel + BackgroundService.
        // Hangfire'dan (ve Postgres'ten) BİLİNÇLİ olarak vazgeçildi --
        // taşınabilir/tek kullanıcılı dağıtımda Hangfire'ın dashboard'unun
        // getirisi, Docker+Postgres bağımlılığının kurulum maliyetine
        // değmiyordu. Job kalıcılığı hâlâ var (kendi TranscriptionJob
        // tablomuzda, artık SQLite'ta) -- sadece "arka planda kim tetikliyor"
        // katmanı basitleşti. Tek worker: whisper zaten tek GPU/CPU
        // instance'ı, paralel işlemenin bir faydası olmazdı.
        services.AddSingleton<IBackgroundJobQueue, BackgroundJobQueue>();
        services.AddHostedService<TranscriptionJobProcessingService>();

        services.AddScoped<ITranscriptionJobRunner, TranscriptionJobRunner>();

        // Singleton: uygulama boyunca TEK bir kayıt, tüm job'lar için
        // paylaşılan iptal token'ları burada tutuluyor.
        services.AddSingleton<IJobCancellationRegistry, JobCancellationRegistry>();

        return services;
    }
}
