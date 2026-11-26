using VerkkuGames.Domain.Catalog;
using VerkkuGames.Domain.Common;

namespace VerkkuGames.Application.Abstractions;

/// <summary>
/// Enriches game keys with public metadata (release year, genres, multiplayer).
/// Returns null metadata when unavailable — never fabricates values.
/// </summary>
public interface IGameMetadataProvider
{
    Task<Result<GameMetadata?>> GetAsync(long steamAppId, CancellationToken cancellationToken = default);
}
