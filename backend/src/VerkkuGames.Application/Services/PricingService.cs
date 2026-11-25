using Microsoft.Extensions.Options;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Application.Options;
using VerkkuGames.Domain.ValueObjects;

namespace VerkkuGames.Application.Services;

public sealed class PricingService : IPricingService
{
    private readonly PricingOptions _options;

    public PricingService(IOptions<PricingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
    }

    public Money CalculateRetailPrice(decimal providerUnitCostUsd, string? categoryId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(providerUnitCostUsd);

        var effectivePercent = _options.DefaultMarkupPercent;
        if (!string.IsNullOrWhiteSpace(categoryId)
            && _options.CategoryMarkupOverrides.TryGetValue(categoryId, out var overridePercent))
        {
            effectivePercent = overridePercent;
        }

        decimal retailUsd;
        if (effectivePercent == 0m)
        {
            // An explicit 0% override is a deliberate at-cost rule: never force the margin floor on it.
            retailUsd = providerUnitCostUsd;
        }
        else
        {
            retailUsd = Math.Max(
                providerUnitCostUsd * (1m + (effectivePercent / 100m)),
                providerUnitCostUsd + _options.MinimumMarginUsd);
        }

        return Money.Create(retailUsd, CurrencyCode.USD);
    }
}
