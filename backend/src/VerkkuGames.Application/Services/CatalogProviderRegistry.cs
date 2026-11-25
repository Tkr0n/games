using System.Collections.ObjectModel;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Domain.Common;

namespace VerkkuGames.Application.Services;

public sealed class CatalogProviderRegistry : ICatalogProviderRegistry
{
    private readonly ReadOnlyCollection<ICatalogProvider> _providers;
    private readonly Dictionary<Guid, ICatalogProvider> _providersByUid;

    public CatalogProviderRegistry(IEnumerable<ICatalogProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var ordered = new List<ICatalogProvider>();
        var byUid = new Dictionary<Guid, ICatalogProvider>();

        foreach (var provider in providers)
        {
            if (provider is null)
            {
                continue;
            }

            ordered.Add(provider);
            byUid.TryAdd(provider.ProviderUid, provider);
        }

        _providers = ordered.AsReadOnly();
        _providersByUid = byUid;
    }

    public Result<ICatalogProvider> GetByUid(Guid providerUid) =>
        _providersByUid.TryGetValue(providerUid, out var provider)
            ? Result<ICatalogProvider>.Success(provider)
            : Result<ICatalogProvider>.Failure(
                Error.NotFound(
                    "provider_not_found",
                    "The requested catalog provider was not found."));

    public Result<ICatalogProvider> GetDefault() =>
        _providers.Count > 0
            ? Result<ICatalogProvider>.Success(_providers[0])
            : Result<ICatalogProvider>.Failure(
                Error.ProviderUnavailable(
                    "no_catalog_provider",
                    "No catalog provider is configured."));

    public IReadOnlyList<ICatalogProvider> GetAll() => _providers;
}
