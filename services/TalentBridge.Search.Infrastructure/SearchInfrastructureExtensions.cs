using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TalentBridge.Search.Infrastructure.Persistence;
using TalentBridge.Search.Infrastructure.Projections;

namespace TalentBridge.Search.Infrastructure;

public static class SearchInfrastructureExtensions
{
    /// <summary>
    /// One pooled <see cref="NpgsqlDataSource"/> for the whole process. There is
    /// deliberately no DbContext on this side: every query is hand-written SQL
    /// through Dapper against the denormalised read model.
    /// </summary>
    public static IServiceCollection AddSearchInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
        services.AddSingleton<JobProjectionHandler>();
        services.AddHealthChecks().AddCheck<DataSourceHealthCheck>("database", tags: ["ready"]);
        return services;
    }
}
