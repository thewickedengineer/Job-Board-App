using System.Reflection;
using Npgsql;

namespace TalentBridge.Search.Infrastructure.Persistence;

/// <summary>
/// Applies <c>db/search-schema.sql</c> (embedded at build time). Every statement
/// in that file is <c>IF NOT EXISTS</c>, so this is safe to run on every start
/// and is how the read model reaches a database that compose did not init
/// (Supabase mode, Testcontainers).
/// </summary>
public static class SearchSchema
{
    public static string Sql { get; } = Load();

    public static async Task ApplyAsync(NpgsqlDataSource dataSource, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(Sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("search-schema.sql")
            ?? throw new InvalidOperationException("Embedded resource search-schema.sql is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
