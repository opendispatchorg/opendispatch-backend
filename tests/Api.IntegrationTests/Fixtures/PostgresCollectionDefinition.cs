namespace OpenDispatch.Api.IntegrationTests.Fixtures;

/// <summary>
/// Binds <see cref="PostgresFixture"/> to a collection so xunit creates it once and hands
/// the same instance to every test class marked
/// <c>[Collection(PostgresCollectionDefinition.Name)]</c>. Classes in this collection do
/// not run in parallel with each other.
/// </summary>
/// <remarks>
/// xunit's convention would name this <c>PostgresCollection</c>, but CA1711 reserves that
/// suffix for actual collection types, so the attribute's own name is used instead.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
