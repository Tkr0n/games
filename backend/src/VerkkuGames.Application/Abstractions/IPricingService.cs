using VerkkuGames.Domain.ValueObjects;

namespace VerkkuGames.Application.Abstractions;

public interface IPricingService
{
    Money CalculateRetailPrice(decimal providerUnitCostUsd, string? categoryId = null);
}
