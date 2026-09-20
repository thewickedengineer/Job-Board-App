using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace TalentBridge.Search.Infrastructure.Persistence;

/// <summary>Readiness: one round trip through the pooled data source.</summary>
public sealed class DataSourceHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("select 1");
            await command.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database unreachable.", ex);
        }
    }
}
