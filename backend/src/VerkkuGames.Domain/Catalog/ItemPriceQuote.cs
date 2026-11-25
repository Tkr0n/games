using VerkkuGames.Domain.ValueObjects;

namespace VerkkuGames.Domain.Catalog;

public sealed record ItemPriceQuote(
    Guid ProviderUid,
    string ExternalItemId,
    string ItemName,
    Money RetailPriceUsd,
    bool InStock,
    DateTimeOffset QuotedAtUtc);
