using System.Linq.Expressions;
using Application.Abstractions.Repositories.Strategies;

namespace Application.DTOs;

public sealed record PagedQueryDto<T>(
    int Take,
    int Skip,
    Expression<Func<T, string>> OrderBy) : IPagedQuery<T>;