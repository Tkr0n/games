using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Infrastructure.Options;

namespace VerkkuGames.Infrastructure.Caching;

public sealed class RedisHybridCacheService : ICacheService
{
    private const int MaxLocalCacheSeconds = 60;

    private readonly HybridCache _cache;
    private readonly CacheOptions _options;

    public RedisHybridCacheService(HybridCache cache, IOptions<CacheOptions> options)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);

        _cache = cache;
        _options = options.Value;
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);

        var prefixedKey = string.Concat(_options.KeyPrefix, key);
        var entryOptions = new HybridCacheEntryOptions
        {
            Expiration = ttl,
            LocalCacheExpiration = TimeSpan.FromSeconds(Math.Min(ttl.TotalSeconds, MaxLocalCacheSeconds)),
        };

        return await _cache
            .GetOrCreateAsync(prefixedKey, factory, entryOptions, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
