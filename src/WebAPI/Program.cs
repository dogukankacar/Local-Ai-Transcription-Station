using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Psikoloji.Application;
using Psikoloji.Application.Common.Interfaces;
using Psikoloji.Domain.Enums;
using Psikoloji.Infrastructure;
using Psikoloji.Infrastructure.Persistence;
using Serilog;

// --- Loglamayı KONSOL yerine DOSYAYA yönlendiriyoruz ---
// OutputType=WinExe ile derlendiğinde (konsol penceresi açılmasın diye)
// .NET'in varsayılan konsol loglayıcısı, geçersiz bir konsol handle'ına
// yazmaya çalışıp SESSİZCE ÇÖKÜYORDU -- "WebAPI.exe hiç açılmıyor"
// sorununun asıl sebebi buydu. Serilog'un dosya loglayıcısı konsol
// handle'ına hiç dokunmadığı için bu sorunu tamamen ortadan kaldırıyor,
// üstelik bir sorun çıkarsa artık "logs\" klasöründeki dosyaya bakılabiliyor.
var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logDirectory);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(
        Path.Combine(logDirectory, "webapi-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// Port'u BURADA, kodun içinde sabitliyoruz -- launchSettings.json SADECE
// "dotnet run" ile çalışır, paketlenmiş (publish) .exe'yi hiç etkilemez.
// Bu satır olmadan publish edilmiş .exe, ASP.NET Core'un varsayılan
// portuna (genelde 5000 civarı) döner ve React arayüzü yanlış porta
// bağlanmaya çalışır -- "Failed to fetch" hatasının asıl sebebi buydu.
builder.WebHost.UseUrls("http://127.0.0.1:5169");

// --- MVC Controllers ---
builder.Services.AddControllers();

// --- Büyük video/ses dosyaları için multipart form limiti ---
// ASP.NET Core'un varsayılan multipart body limiti 128MB'dır ve bu,
// Controller'daki [RequestSizeLimit] attribute'ünden TAMAMEN AYRI bir
// sınırdır -- ikisi de aşılmadan büyük dosya kabul edilmez. Laboratuvar
// kayıtları saatlerce sürebildiği için (kolayca birkaç GB) bunu 4GB'a
// çıkarıyoruz.
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 4L * 1024 * 1024 * 1024; // 4 GB
});

// --- CORS: masaüstü UI (Tauri/Electron webview'i) farklı bir origin'den
// istek atıyor, bu politika olmadan tarayıcı tüm istekleri sessizce engeller.
builder.Services.AddCors(options =>
{
    options.AddPolicy("DesktopApp", policy =>
    {
        policy.WithOrigins(
                "http://localhost:1420",    // Vite dev server (Tauri varsayılanı)
                "tauri://localhost",        // Tauri production (Linux/macOS)
                "http://tauri.localhost",   // Tauri production (Windows, Tauri 2.0+ varsayılanı)
                "https://tauri.localhost")  // Tauri production (Windows, useHttpsScheme=true ise)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// --- Swagger ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- Application + Infrastructure katmanları (kendi DI extension'larımız) ---
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

var app = builder.Build();

// --- Yarım kalan job temizliği (SADECE bir kere, uygulama başlarken) ---
// Bu, önceki "arka planda benden habersiz devam etme" davranışının TAM
// TERSİ: hiçbir işi DEVAM ETTİRMİYOR ya da TEKRAR BAŞLATMIYOR. Sadece
// dürüst bir muhasebe yapıyor -- uygulama YENİ başladığı için, DB'de
// "Processing" görünen bir job varsa, bu kesinlikle önceki bir process
// ömründen kalma bir kayıttır (bu taze process henüz hiçbir iş
// dağıtmadı) -- gerçekte hiç kimse onu işlemiyor. Onu "Failed" olarak
// işaretleyip, kullanıcının artık Hangfire dashboard'una girip elle
// temizlemesine gerek bırakmıyoruz.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // --- Otomatik migration (SADECE bir kere, uygulama başlarken) ---
    // Geliştirme sırasında `dotnet ef database update` komutunu elle
    // çalıştırıyorduk -- ama paketlenmiş .exe'yi kullanacak akademisyende
    // `dotnet ef` aracı HİÇ olmayacak. Bu yüzden uygulama, kendi kendine
    // (ilk açılışta psikoloji.db dosyasını sıfırdan oluşturarak, sonraki
    // açılışlarda varsa yeni migration'ları uygulayarak) veritabanını
    // güncel tutuyor.
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
    logger.LogInformation("Veritabanı migration'ları uygulandı (psikoloji.db).");

    // --- Yarım kalan job temizliği ---
    // Bu, önceki "arka planda benden habersiz devam etme" davranışının TAM
    // TERSİ: hiçbir işi DEVAM ETTİRMİYOR ya da TEKRAR BAŞLATMIYOR. Sadece
    // dürüst bir muhasebe yapıyor -- uygulama YENİ başladığı için, DB'de
    // "Processing" görünen bir job varsa, bu kesinlikle önceki bir process
    // ömründen kalma bir kayıttır (bu taze process henüz hiçbir iş
    // dağıtmadı) -- gerçekte hiç kimse onu işlemiyor. Onu "Failed" olarak
    // işaretleyip, kullanıcının artık elle temizlemesine gerek bırakmıyoruz.
    var staleJobs = await db.TranscriptionJobs
        .Where(j => j.Status == JobStatus.Processing)
        .ToListAsync();

    foreach (var job in staleJobs)
    {
        job.Status = JobStatus.Failed;
        job.ErrorMessage = "Uygulama yeniden başlatıldığı için yarım kalan işlem kesildi.";
        job.CompletedAtUtc = DateTime.UtcNow;
    }

    if (staleJobs.Count > 0)
    {
        await db.SaveChangesAsync();
        logger.LogInformation(
            "{Count} adet yarım kalmış (Processing) job başlangıçta 'Failed' olarak işaretlendi.",
            staleJobs.Count);
    }
}

// Swagger sadece Development ortamında açık -- test ederken
// ASPNETCORE_ENVIRONMENT=Development olduğundan emin ol
// (Visual Studio/Rider'da varsayılan olarak zaten öyledir).
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("DesktopApp");
app.UseAuthorization();

app.MapControllers();

try
{
    app.Run();
}
catch (Exception ex)
{
    // Uygulama daha ayağa kalkmadan çökerse (ör. port çakışması), bu
    // hatanın da dosyaya yazıldığından emin oluyoruz -- WinExe modunda
    // konsol olmadığı için başka hiçbir yerde görünmezdi.
    Log.Fatal(ex, "Uygulama başlatılırken kritik hata oluştu.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
