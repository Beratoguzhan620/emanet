using System.Collections.Concurrent;
using Emanet.Infrastructure.Storage;

namespace Emanet.IntegrationTests.Support;

public sealed class MemoryFileStore : IFileStore
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public bool Has(string key) => _files.ContainsKey(key);

    public Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct)
    {
        _files[key] = content.ToArray();
        return Task.CompletedTask;
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken ct) =>
        Task.FromResult(_files.TryGetValue(key, out var bytes) ? bytes.ToArray() : null);

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        _files.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
