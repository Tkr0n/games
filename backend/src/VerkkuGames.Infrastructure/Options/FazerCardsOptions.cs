using System.ComponentModel.DataAnnotations;

namespace VerkkuGames.Infrastructure.Options;

public sealed class FazerCardsOptions : IValidatableObject
{
    public const string SectionName = "FazerCards";

    [Required]
    [Url]
    public string BaseUrl { get; set; } = "https://api.fzr.cards/api/v2";

    public string ApiKey { get; set; } = string.Empty;

    public Guid ProviderUid { get; set; } = Guid.Parse("9075f324-4177-45e9-a79c-f23722258b8f");

    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 8;

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
