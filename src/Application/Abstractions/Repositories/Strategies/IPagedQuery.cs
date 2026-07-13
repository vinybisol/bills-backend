using System.Linq.Expressions;

namespace Application.Abstractions.Repositories.Strategies;

public interface IPagedQuery<T>
{
    int Take { get; }
    int Skip { get; }
    Expression<Func<T, string>> OrderBy { get; }
}