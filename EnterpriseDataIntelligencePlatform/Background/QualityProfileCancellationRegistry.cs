using System.Collections.Concurrent;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;

namespace EnterpriseDataIntelligencePlatform.Background;

public sealed class QualityProfileCancellationRegistry : IQualityProfileCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _tokens = new();

    public CancellationToken Register(Guid profileRunId, CancellationToken hostToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        if (!_tokens.TryAdd(profileRunId, source))
        {
            source.Dispose();
            throw new InvalidOperationException($"Profile run {profileRunId} is already registered.");
        }
        return source.Token;
    }

    public bool Cancel(Guid profileRunId)
    {
        if (!_tokens.TryGetValue(profileRunId, out var source)) return false;
        source.Cancel();
        return true;
    }

    public void Unregister(Guid profileRunId)
    {
        if (_tokens.TryRemove(profileRunId, out var source)) source.Dispose();
    }
}
