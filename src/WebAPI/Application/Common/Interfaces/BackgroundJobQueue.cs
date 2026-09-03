using System.Threading.Channels;
using Psikoloji.Application.Common.Interfaces;

namespace Psikoloji.Infrastructure.BackgroundJobs;

/// <summary>
/// Basit, tek-instance bir uygulama için yeterli olan bellek-içi kuyruk.
/// Uygulama yeniden başlarsa kuyruktaki bekleyen (henüz dequeue edilmemiş)
/// işler kaybolur -- ama Pending durumda kalan job'lar DB'de görünür kalır
/// ve UI/API'den görülebilir (sadece otomatik devam etmez, kullanıcı
/// bilerek tekrar tetiklemesi gerekir -- bu bilinçli bir tercih, bkz.
/// TranscriptionJobRunner'daki güvenlik ağı).
/// </summary>
public sealed class BackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(jobId, cancellationToken);

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
