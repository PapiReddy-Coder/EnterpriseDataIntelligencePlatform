using System.Threading.Channels;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;

namespace EnterpriseDataIntelligencePlatform.Background;

public sealed class ImportJobQueue : IImportJobQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    public ValueTask EnqueueAsync(Guid importId, CancellationToken ct) =>
        _channel.Writer.WriteAsync(importId, ct);

    public ValueTask<Guid> DequeueAsync(CancellationToken ct) =>
        _channel.Reader.ReadAsync(ct);
}
