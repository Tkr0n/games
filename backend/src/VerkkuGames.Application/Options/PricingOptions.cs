using System.ComponentModel.DataAnnotations;

namespace VerkkuGames.Application.Options;

public sealed class PricingOptions
{
    public const string SectionName = "Pricing";

    [Range(0, 500)]
    public decimal DefaultMarkupPercent { get; set; } = 10.0m;

    [Range(0, 1000)]
    public decimal MinimumMarginUsd { get; set; } = 0.25m;

    public Dictionary<string, decimal> CategoryMarkupOverrides { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}
