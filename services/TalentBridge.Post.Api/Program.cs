using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using TalentBridge.Post.Api.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddPostDatabase();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Liveness: the process is up. Runs no checks so it never depends on the database.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });

// Readiness: the database is reachable.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

await app.ApplyMigrationsIfConfiguredAsync();

await app.RunAsync();

public partial class Program;
