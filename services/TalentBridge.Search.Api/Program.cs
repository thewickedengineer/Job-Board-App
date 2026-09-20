using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;
using Serilog.Formatting.Compact;
using TalentBridge.Search.Api.Configuration;
using TalentBridge.Search.Api.Errors;
using TalentBridge.Search.Api.Projections;
using TalentBridge.Search.Infrastructure;
using TalentBridge.Search.Infrastructure.Persistence;

// Local `dotnet run`: the projection secret comes from the repo-root .env.
DotEnv.Load(Directory.GetCurrentDirectory());

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) =>
{
    logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", "search-api");

    if (context.HostingEnvironment.IsDevelopment())
    {
        logger.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
    }
    else
    {
        logger.WriteTo.Console(new RenderedCompactJsonFormatter());
    }
});

builder.Services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<ProjectionOptions>().BindConfiguration(ProjectionOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<CorsOptions>().BindConfiguration(CorsOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

// The data source needs the connection string at registration time; a missing
// value still fails fast because the options validator runs at startup.
builder.Services.AddSearchInfrastructure(builder.Configuration[$"{DatabaseOptions.SectionName}:ConnectionString"] ?? string.Empty);

builder.Services.AddCors(cors => cors.AddPolicy(CorsOptions.PolicyName, policy =>
{
    var origins = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>()?.AllowedOrigins ?? [];
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("ETag");
}));

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = ctx =>
        ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging(options =>
    options.EnrichDiagnosticContext = (diagnostic, http) =>
        diagnostic.Set("TraceId", Activity.Current?.Id ?? http.TraceIdentifier));
app.UseCors(CorsOptions.PolicyName);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapProjectionEndpoints();

var database = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
if (app.Environment.IsDevelopment() && database.ApplySchemaOnStartup)
{
    app.Logger.LogInformation("Applying search schema (Development, Database:ApplySchemaOnStartup=true)");
    await SearchSchema.ApplyAsync(app.Services.GetRequiredService<NpgsqlDataSource>());
}

await app.RunAsync();

public partial class Program;
