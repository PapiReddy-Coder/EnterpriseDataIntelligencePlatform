using System.Collections.Concurrent;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;

namespace EnterpriseDataIntelligencePlatform.Background;

public sealed class ImportCancellationRegistry : IImportCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _tokens = new();

    public CancellationToken Register(Guid importId, CancellationToken hostToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        if (!_tokens.TryAdd(importId, source))
        {
            source.Dispose();
            return _tokens[importId].Token;
        }

        return source.Token;
    }

    public bool RequestCancellation(Guid importId)
    {
        if (!_tokens.TryGetValue(importId, out var source))
        {
            return false;
        }

        source.Cancel();
        return true;
    }

    public void Unregister(Guid importId)
    {
        if (_tokens.TryRemove(importId, out var source))
        {
            source.Dispose();
        }
    }
}
