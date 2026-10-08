using System.Linq.Expressions;

namespace Application.Abstractions.Repositories.Strategies;

public interface IPagedQuery<TIn, TResult>
{
    int Take { get; }
    int Skip { get; }
    Expression<Func<TIn, TResult>> OrderBy { get; }
}