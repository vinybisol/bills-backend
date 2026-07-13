using Domain.Abstractions.Filters;

namespace Api.Filters;

/// <summary>Default scoped implementation of <see cref="ICurrentOwner"/>.</summary>
internal sealed record CurrentOwner : ICurrentOwner
{
    public long Id { get; private set; }

    public void SetCurrentOwnerId(long id)
         => Id = id;
}