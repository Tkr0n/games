namespace VerkkuGames.Application.Abstractions;

public interface ICacheService
{
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);
}
