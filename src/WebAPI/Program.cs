using Hangfire;
using Microsoft.AspNetCore.Http.Features;
using Psikoloji.Application;
using Psikoloji.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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
                "https://tauri.localhost")  // Tauri production (Windows)
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

// Hangfire dashboard -- job geçmişi, hatalar, retry'lar burada görünür.
// Sadece 127.0.0.1'e bağlı olduğumuz için (KVKK/yerel izolasyon), ekstra
// bir yetkilendirme katmanına şimdilik gerek yok; ileride dışarıya açık
// bir sunucuya taşırsan DashboardOptions.Authorization eklemen gerekir.
app.UseHangfireDashboard("/hangfire");

app.Run();
