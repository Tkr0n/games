using VerkkuGames.Domain.ValueObjects;

namespace VerkkuGames.Domain.Catalog;

public sealed record CategoryRequirement(
    string FieldName,
    RequirementDataType DataType,
    string? Placeholder,
    IReadOnlyList<string> SelectOptions,
    int? MinLength,
    int? MaxLength);
