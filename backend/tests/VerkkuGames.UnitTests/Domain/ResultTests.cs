using VerkkuGames.Domain.Common;
using Xunit;

namespace VerkkuGames.UnitTests.Domain;

public sealed class ResultTests
{
    [Fact]
    public void Success_ExposesValue()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Failure_AccessingValueThrowsInvalidOperationException()
    {
        var result = Result<int>.Failure(Error.NotFound("item_not_found", "Item not found."));

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Success_AccessingErrorThrowsInvalidOperationException()
    {
        var result = Result<string>.Success("ok");

        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    [Fact]
    public void Failure_ExposesErrorWithRetryAfter()
    {
        var result = Result<string>.Failure(Error.RateLimited("rate_limited", "Too many requests.", 30));

        Assert.Equal("rate_limited", result.Error.Code);
        Assert.Equal("Too many requests.", result.Error.Message);
        Assert.Equal(ErrorType.RateLimited, result.Error.Type);
        Assert.Equal(30, result.Error.RetryAfterSeconds);
    }

    [Fact]
    public void Match_InvokesSuccessOrFailureBranch()
    {
        var onSuccess = Result<int>.Success(2).Match(value => value * 10, _ => -1);
        var onFailure = Result<int>
            .Failure(Error.Validation("invalid_amount", "Invalid amount."))
            .Match(value => value * 10, error => error.Type == ErrorType.Validation ? 7 : -1);

        Assert.Equal(20, onSuccess);
        Assert.Equal(7, onFailure);
    }

    [Fact]
    public void ErrorFactories_SetExpectedTypeAndRetryAfterDefaults()
    {
        Assert.Equal(ErrorType.Validation, Error.Validation("code", "message").Type);
        Assert.Equal(ErrorType.NotFound, Error.NotFound("code", "message").Type);
        Assert.Equal(ErrorType.Unauthorized, Error.Unauthorized("code", "message").Type);
        Assert.Equal(ErrorType.Forbidden, Error.Forbidden("code", "message").Type);
        Assert.Equal(ErrorType.ProviderUnavailable, Error.ProviderUnavailable("code", "message").Type);
        Assert.Equal(ErrorType.PricesLocked, Error.PricesLocked("code", "message").Type);

        var rateLimited = Error.RateLimited("code", "message", retryAfterSeconds: 15);
        Assert.Equal(ErrorType.RateLimited, rateLimited.Type);
        Assert.Equal(15, rateLimited.RetryAfterSeconds);

        var conflictWithoutRetry = Error.Conflict("code", "message");
        Assert.Equal(ErrorType.Conflict, conflictWithoutRetry.Type);
        Assert.Null(conflictWithoutRetry.RetryAfterSeconds);

        var conflictWithRetry = Error.Conflict("code", "message", 5);
        Assert.Equal(5, conflictWithRetry.RetryAfterSeconds);
    }
}
