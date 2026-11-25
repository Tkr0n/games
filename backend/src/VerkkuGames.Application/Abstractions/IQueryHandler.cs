using VerkkuGames.Domain.Common;

namespace VerkkuGames.Application.Abstractions;

public interface IQueryHandler<in TQuery, TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}
