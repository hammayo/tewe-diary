using Microsoft.AspNetCore.OutputCaching;

namespace Movies.Api.Tests;

// Records evicted tags so tests can assert the endpoint dropped the right cache entries.
// Get/Set are inert — the admin endpoint under test isn't cached.
public class SpyOutputCacheStore : IOutputCacheStore
{
    public List<string> EvictedTags { get; } = new();

    public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
    {
        EvictedTags.Add(tag);
        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) =>
        ValueTask.FromResult<byte[]?>(null);

    public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
