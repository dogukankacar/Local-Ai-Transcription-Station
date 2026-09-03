namespace Psikoloji.Application.Common.Interfaces;

/// <summary>
/// Aktif çalışan job'lar için CancellationToken tutan bellek-içi kayıt.
/// Hangfire'ın kendi serileştirdiği CancellationToken parametresi (her
/// zaman CancellationToken.None gelir, gerçek bir iptal sinyali taşımaz)
/// yerine, gerçek zamanlı iptal için bunu kullanıyoruz. Uygulama yeniden
/// başlarsa kayıt sıfırlanır -- o an çalışan bir job varsa ve uygulama
/// yeniden başladıysa, o job için artık iptal sinyali gönderilemez (bilinen
/// bir sınır, tek makinelik bu araç için kabul edilebilir).
/// </summary>
public interface IJobCancellationRegistry
{
    /// <summary>Job için yeni bir CancellationToken kaydeder ve döner.</summary>
    CancellationToken Register(Guid jobId);

    /// <summary>Kayıtlıysa job'a iptal sinyali gönderir. Kayıtlı değilse (henüz
    /// başlamamış ya da zaten bitmiş) hiçbir şey yapmaz, false döner.</summary>
    bool Cancel(Guid jobId);

    /// <summary>Job bitince (başarı/hata/iptal fark etmeksizin) kaydı temizler.</summary>
    void Unregister(Guid jobId);
}
