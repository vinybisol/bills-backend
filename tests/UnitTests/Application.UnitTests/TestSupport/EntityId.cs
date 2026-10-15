namespace Application.UnitTests.TestSupport;

/// <summary>
/// Simulates the database-generated <c>Id</c> of a domain entity, whose setter is private.
/// </summary>
internal static class EntityId
{
    public static T With<T>(T entity, long id) where T : class
    {
        var property = typeof(T).GetProperty("Id")
            ?? throw new InvalidOperationException($"{typeof(T).Name} has no Id property.");
        property.SetValue(entity, id);
        return entity;
    }
}
