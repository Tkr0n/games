using VerkkuGames.Domain.Common;

namespace VerkkuGames.Application.Abstractions;

public interface ICatalogProviderRegistry
{
    Result<ICatalogProvider> GetByUid(Guid providerUid);

    Result<ICatalogProvider> GetDefault();

    IReadOnlyList<ICatalogProvider> GetAll();
}
