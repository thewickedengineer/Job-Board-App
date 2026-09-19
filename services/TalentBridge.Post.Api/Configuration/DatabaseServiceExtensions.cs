using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TalentBridge.Post.Infrastructure.Persistence;

namespace TalentBridge.Post.Api.Configuration;

public static class DatabaseServiceExtensions
{
    public static IServiceCollection AddPostDatabase(this IServiceCollection services)
    {
        // ValidateOnStart makes a missing connection string a startup failure, not
        // a first-request failure.
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<PostDbContext>((sp, options) =>
        {
            var db = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            PostDbContextOptions.Configure(options, db.ConnectionString);
        });

        services.AddHealthChecks()
            .AddDbContextCheck<PostDbContext>("database", tags: ["ready"]);

        return services;
    }

    public static async Task ApplyMigrationsIfConfiguredAsync(this WebApplication app)
    {
        var db = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        if (!app.Environment.IsDevelopment() || !db.ApplyMigrationsOnStartup)
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PostDbContext>();
        app.Logger.LogInformation("Applying pending EF Core migrations (Development, Database:ApplyMigrationsOnStartup=true)");
        await context.Database.MigrateAsync();
    }
}
