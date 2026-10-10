namespace MyBank.Tests.Integration;

/// <summary>Coleção do xUnit que compartilha uma única <see cref="ApiFixture"/> (API + PostgreSQL) entre as classes de teste.</summary>
[CollectionDefinition(Name)]
public sealed class SharedApi : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
