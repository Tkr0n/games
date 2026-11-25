using VerkkuGames.Application.Catalog.Queries;
using VerkkuGames.Domain.ValueObjects;
using Xunit;

namespace VerkkuGames.UnitTests.Application;

public sealed class QueryValidatorsTests
{
    private static readonly Guid ProviderUid = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    [Theory]
    [InlineData(0, 24)]
    [InlineData(-1, 24)]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    [InlineData(1, 101)]
    public void GetCatalogItemsQuery_InvalidPaging_FailsValidation(int page, int pageSize)
    {
        var validator = new GetCatalogItemsQueryValidator();

        var result = validator.Validate(new GetCatalogItemsQuery(Page: page, PageSize: pageSize));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 100)]
    [InlineData(3, 24)]
    public void GetCatalogItemsQuery_ValidPaging_PassesValidation(int page, int pageSize)
    {
        var validator = new GetCatalogItemsQueryValidator();

        var result = validator.Validate(new GetCatalogItemsQuery(Page: page, PageSize: pageSize));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void GetCatalogItemsQuery_SearchLongerThan100_FailsValidation()
    {
        var validator = new GetCatalogItemsQueryValidator();

        var result = validator.Validate(new GetCatalogItemsQuery(Search: new string('a', 101)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(GetCatalogItemsQuery.Search));
    }

    [Fact]
    public void GetCatalogItemsQuery_SearchOf100_PassesValidation()
    {
        var validator = new GetCatalogItemsQueryValidator();

        var result = validator.Validate(new GetCatalogItemsQuery(Search: new string('a', 100)));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("abc/../etc")]
    [InlineData("abc def")]
    [InlineData("abc;DROP")]
    [InlineData("abc$def")]
    [InlineData("<script>")]
    public void GetCatalogItemsQuery_CategoryIdWithInjectionCharacters_FailsValidation(string categoryId)
    {
        var validator = new GetCatalogItemsQueryValidator();

        var result = validator.Validate(new GetCatalogItemsQuery(CategoryId: categoryId));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(GetCatalogItemsQuery.CategoryId));
    }

    [Fact]
    public void GetCatalogItemsQuery_CategoryIdLongerThan64_FailsValidation()
    {
        var validator = new GetCatalogItemsQueryValidator();

        var result = validator.Validate(new GetCatalogItemsQuery(CategoryId: new string('a', 65)));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("abc-123_X")]
    [InlineData("ABC_xyz-9")]
    public void GetCatalogItemsQuery_ValidCategoryId_PassesValidation(string categoryId)
    {
        var validator = new GetCatalogItemsQueryValidator();

        var result = validator.Validate(new GetCatalogItemsQuery(CategoryId: categoryId));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void GetCatalogItemsQuery_EmptyProviderUid_FailsValidation()
    {
        var validator = new GetCatalogItemsQueryValidator();

        var result = validator.Validate(new GetCatalogItemsQuery(ProviderUid: Guid.Empty));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetCatalogItemsQuery_NullProviderUid_PassesValidation()
    {
        var validator = new GetCatalogItemsQueryValidator();

        var result = validator.Validate(new GetCatalogItemsQuery());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void GetCategoriesQuery_InvalidBigCategoryIdAndSearch_FailsValidation()
    {
        var validator = new GetCategoriesQueryValidator();

        var result = validator.Validate(new GetCategoriesQuery(BigCategoryId: "../escape", Search: new string('x', 101)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(GetCategoriesQuery.BigCategoryId));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(GetCategoriesQuery.Search));
    }

    [Fact]
    public void GetCategoriesQuery_ValidFilters_PassesValidation()
    {
        var validator = new GetCategoriesQueryValidator();

        var result = validator.Validate(new GetCategoriesQuery(
            ProviderUid: ProviderUid,
            BigCategoryId: "games",
            Region: RegionCode.USA,
            IncludeGlobal: true,
            Search: "gift card"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void GetCategoriesQuery_EmptyProviderUid_FailsValidation()
    {
        var validator = new GetCategoriesQueryValidator();

        var result = validator.Validate(new GetCategoriesQuery(ProviderUid: Guid.Empty));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetBigCategoriesQuery_EmptyProviderUid_FailsValidation()
    {
        var validator = new GetBigCategoriesQueryValidator();

        var result = validator.Validate(new GetBigCategoriesQuery(Guid.Empty));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetBigCategoriesQuery_NullProviderUid_PassesValidation()
    {
        var validator = new GetBigCategoriesQueryValidator();

        var result = validator.Validate(new GetBigCategoriesQuery());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc/def")]
    [InlineData("abc def")]
    [InlineData("../../../etc/passwd")]
    public void GetCatalogItemDetailQuery_InvalidItemId_FailsValidation(string itemId)
    {
        var validator = new GetCatalogItemDetailQueryValidator();

        var result = validator.Validate(new GetCatalogItemDetailQuery(ProviderUid, itemId));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetCatalogItemDetailQuery_EmptyProviderUid_FailsValidation()
    {
        var validator = new GetCatalogItemDetailQueryValidator();

        var result = validator.Validate(new GetCatalogItemDetailQuery(Guid.Empty, "sku-1"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(GetCatalogItemDetailQuery.ProviderUid));
    }

    [Fact]
    public void GetCatalogItemDetailQuery_ValidValues_PassesValidation()
    {
        var validator = new GetCatalogItemDetailQueryValidator();

        var result = validator.Validate(new GetCatalogItemDetailQuery(ProviderUid, "sku-1_A"));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sku with space")]
    [InlineData("sku|1")]
    public void GetItemPriceQuery_InvalidItemId_FailsValidation(string itemId)
    {
        var validator = new GetItemPriceQueryValidator();

        var result = validator.Validate(new GetItemPriceQuery(ProviderUid, itemId));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetItemPriceQuery_EmptyProviderUid_FailsValidation()
    {
        var validator = new GetItemPriceQueryValidator();

        var result = validator.Validate(new GetItemPriceQuery(Guid.Empty, "sku-1"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetItemPriceQuery_ValidValues_PassesValidation()
    {
        var validator = new GetItemPriceQueryValidator();

        var result = validator.Validate(new GetItemPriceQuery(ProviderUid, "sku-1"));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("cat/1")]
    [InlineData("cat 1")]
    public void GetCategoryRequirementsQuery_InvalidCategoryId_FailsValidation(string categoryId)
    {
        var validator = new GetCategoryRequirementsQueryValidator();

        var result = validator.Validate(new GetCategoryRequirementsQuery(ProviderUid, categoryId));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetCategoryRequirementsQuery_EmptyProviderUid_FailsValidation()
    {
        var validator = new GetCategoryRequirementsQueryValidator();

        var result = validator.Validate(new GetCategoryRequirementsQuery(Guid.Empty, "cat-1"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetCategoryRequirementsQuery_ValidValues_PassesValidation()
    {
        var validator = new GetCategoryRequirementsQueryValidator();

        var result = validator.Validate(new GetCategoryRequirementsQuery(ProviderUid, "cat-1"));

        Assert.True(result.IsValid);
    }
}
