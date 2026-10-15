using System.Linq.Expressions;
using Application.Abstractions.Repositories.Strategies;

namespace Application.DTOs;

public sealed record PagedQueryDto<TIn, TResult>(
    int Take,
    int Skip,
    Expression<Func<TIn, TResult>> OrderBy) : IPagedQuery<TIn, TResult>;