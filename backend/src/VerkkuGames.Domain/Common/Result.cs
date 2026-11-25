using System.Diagnostics.CodeAnalysis;

namespace VerkkuGames.Domain.Common;

[SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "Success and Failure are the contractually agreed factory surface of Result<T>.")]
public sealed class Result<T>
{
    private readonly T _value;
    private readonly Error? _error;

    private Result(bool isSuccess, T value, Error? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("The value of a failed result cannot be accessed.");

    public Error Error => _error
        ?? throw new InvalidOperationException("The error of a successful result cannot be accessed.");

    public static Result<T> Success(T value) => new(true, value, null);

    public static Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new Result<T>(false, default!, error);
    }

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess(_value) : onFailure(Error);
    }
}
