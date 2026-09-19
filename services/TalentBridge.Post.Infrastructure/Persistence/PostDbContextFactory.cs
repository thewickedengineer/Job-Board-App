using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TalentBridge.Post.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> at design time so migrations can be generated
/// without booting the API. Reads <c>POST_DB_CONNECTION_STRING</c>; falls back to
/// the docker-compose local Postgres defaults.
/// </summary>
public sealed class PostDbContextFactory : IDesignTimeDbContextFactory<PostDbContext>
{
    public const string DefaultLocalConnectionString =
        "Host=localhost;Port=5432;Database=talentbridge;Username=talentbridge;Password=talentbridge";

    public PostDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("POST_DB_CONNECTION_STRING")
            ?? DefaultLocalConnectionString;

        var options = new DbContextOptionsBuilder<PostDbContext>();
        PostDbContextOptions.Configure(options, connectionString);
        return new PostDbContext(options.Options);
    }
}
