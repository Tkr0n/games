using System.ComponentModel.DataAnnotations;

namespace VerkkuGames.Infrastructure.Options;

public sealed class CacheOptions : IValidatableObject
{
    public const string SectionName = "Cache";

    public string RedisConnectionString { get; set; } = string.Empty;

    public bool UseRedis { get; set; } = true;

    [Required]
    public string KeyPrefix { get; set; } = "verkkugames:v1:";

    [Range(5, 86400)]
    public int BigCategoriesSeconds { get; set; } = 1800;

    [Range(5, 86400)]
    public int CategoriesSeconds { get; set; } = 900;

    [Range(5, 86400)]
    public int ItemsSeconds { get; set; } = 600;

    [Range(5, 86400)]
    public int RequirementsSeconds { get; set; } = 1800;

    [Range(5, 86400)]
    public int ItemPriceSeconds { get; set; } = 60;

    [Range(5, 86400)]
    public int ExchangeRatesSeconds { get; set; } = 60;

    [Range(5, 86400)]
    public int AccountHealthSeconds { get; set; } = 300;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (UseRedis && string.IsNullOrWhiteSpace(RedisConnectionString))
        {
            yield return new ValidationResult(
                "A Redis connection string is required when the distributed cache is enabled.",
                new[] { nameof(RedisConnectionString) });
        }
    }
}
