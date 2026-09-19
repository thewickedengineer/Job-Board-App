using Testcontainers.PostgreSql;

namespace TalentBridge.Post.Tests.Integration;

/// <summary>
/// One throwaway postgres:17 per test run, shared by every class in the
/// "postgres" collection. The API applies its own migrations on startup, so the
/// container starts empty.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("talentbridge")
        .WithUsername("talentbridge")
        .WithPassword("talentbridge")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
