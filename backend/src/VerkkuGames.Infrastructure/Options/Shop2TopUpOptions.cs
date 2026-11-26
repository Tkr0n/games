using System.ComponentModel.DataAnnotations;

namespace VerkkuGames.Infrastructure.Options;

public sealed class Shop2TopUpOptions : IValidatableObject
{
    public const string SectionName = "Shop2TopUp";

    [Required]
    [Url]
    public string BaseUrl { get; set; } = "https://shop2topup.com/api/endpoints/v1";

    [Required]
    public string KeyId { get; set; } = string.Empty;

    [Required]
    public string KeySecret { get; set; } = string.Empty;

    public Guid ProviderUid { get; set; } = Guid.Parse("d4f8a2c1-7b3e-4f90-9a12-6e8b5c3d1f42");

    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 8;

    [Required]
    [Url]
    public string ImageCdnBaseUrl { get; set; } = "https://shop2topup.com/api/public/img/";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ProviderUid == Guid.Empty)
        {
            yield return new ValidationResult(
                "The provider identifier must not be an empty identifier.",
                new[] { nameof(ProviderUid) });
        }
    }
}
