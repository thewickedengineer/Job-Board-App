using Microsoft.EntityFrameworkCore;

namespace TalentBridge.Post.Infrastructure.Persistence;

/// <summary>
/// Single place that knows how the write-side context is configured, shared by
/// the API's DI registration and the design-time factory.
/// </summary>
public static class PostDbContextOptions
{
    public static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations_history", PostDbContext.Schema)
                .MigrationsAssembly(typeof(PostDbContext).Assembly.GetName().Name))
            .UseSnakeCaseNamingConvention();
    }
}
