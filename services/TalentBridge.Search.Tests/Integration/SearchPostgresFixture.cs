using Npgsql;
using TalentBridge.Search.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace TalentBridge.Search.Tests.Integration;

/// <summary>One postgres:17 per run with db/search-schema.sql applied.</summary>
public sealed class SearchPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("talentbridge")
        .WithUsername("talentbridge")
        .WithPassword("talentbridge")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        DataSource = new NpgsqlDataSourceBuilder(ConnectionString).Build();
        await SearchSchema.ApplyAsync(DataSource);
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SearchPostgresCollection : ICollectionFixture<SearchPostgresFixture>
{
    public const string Name = "search-postgres";
}
