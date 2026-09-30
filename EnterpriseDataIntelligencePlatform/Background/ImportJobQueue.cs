using System.Threading.Channels;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;

namespace EnterpriseDataIntelligencePlatform.Background;

public sealed class BackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<BackgroundJob> _channel = Channel.CreateUnbounded<BackgroundJob>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ValueTask EnqueueAsync(BackgroundJob job, CancellationToken ct) =>
        _channel.Writer.WriteAsync(job, ct);

    public ValueTask<BackgroundJob> DequeueAsync(CancellationToken ct) =>
        _channel.Reader.ReadAsync(ct);
}
