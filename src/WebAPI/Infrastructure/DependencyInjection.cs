using Hangfire;
using Hangfire.PostgreSql;
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

        // --- Kalıcılık (Postgres, Docker üzerinde izole -- sadece 127.0.0.1) ---
        var postgresConnectionString = configuration.GetConnectionString("Postgres");
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(postgresConnectionString));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // --- Arka plan iş kuyruğu: Hangfire + aynı Postgres (ayrı Redis'e
        // gerek yok -- Hangfire kendi tablolarını "hangfire" şemasında tutar,
        // EF Core'un tablolarıyla çakışmaz). Bu, job'ların kalıcı olmasını
        // (uygulama/PC yeniden başlasa bile) VE hazır bir dashboard'u aynı
        // anda sağlıyor.
        //
        // InvisibilityTimeout ÇOK UZUN (30 gün) ayarlandı -- Hangfire'ın
        // VARSAYILAN davranışı, bir job "Processing" durumunda kalıp
        // belirli bir süre (varsayılan 30 DAKİKA) hiç heartbeat almazsa
        // (ör. PC çökmesi, terminal kapatma) onu SESSİZCE, KİMSEYE
        // SORMADAN tekrar kuyruğa alıp yeniden çalıştırmaktı. Bu, önceki
        // "kendiliğinden başlayan işlem" gizeminin asıl kaynağıydı. Artık
        // hiçbir job, sen bilerek tekrar tetiklemeden (ya da dashboard'dan
        // elle "Requeue" demeden) tekrar çalışmayacak.
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(pg => pg.UseNpgsqlConnection(postgresConnectionString),
                new PostgreSqlStorageOptions
                {
                    InvisibilityTimeout = TimeSpan.FromDays(30),
                }));

        // Tek makine, tek instance -- fazla worker thread'i GPU/VRAM
        // paylaşımını karmaşıklaştırır, bu yüzden WorkerCount=1 ile aynı anda
        // sadece bir job işleniyor (whisper zaten tek GPU instance'ı).
        services.AddHangfireServer(options => options.WorkerCount = 1);

        services.AddScoped<ITranscriptionJobRunner, TranscriptionJobRunner>();

        return services;
    }
}

