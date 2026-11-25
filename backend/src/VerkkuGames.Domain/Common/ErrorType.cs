namespace VerkkuGames.Domain.Common;

public enum ErrorType
{
    Validation,
    NotFound,
    Unauthorized,
    Forbidden,
    RateLimited,
    Conflict,
    ProviderUnavailable,
    PricesLocked,
}
