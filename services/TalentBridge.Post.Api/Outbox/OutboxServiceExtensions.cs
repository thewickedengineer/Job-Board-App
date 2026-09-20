using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using TalentBridge.Post.Api.Configuration;

namespace TalentBridge.Post.Api.Outbox;

public static class OutboxServiceExtensions
{
    public static IServiceCollection AddOutboxPublisher(this IServiceCollection services)
    {
        services.AddOptions<ProjectionOptions>()
            .BindConfiguration(ProjectionOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OutboxOptions>()
            .BindConfiguration(OutboxOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<ProjectionClient>((sp, http) =>
            {
                var projection = sp.GetRequiredService<IOptions<ProjectionOptions>>().Value;
                http.BaseAddress = new Uri(projection.SearchApiBaseUrl);
                http.DefaultRequestHeaders.Add(ProjectionOptions.HeaderName, projection.SharedSecret);
            })
            // Polly v8 pipeline: rate limiter → total timeout → retry → circuit
            // breaker → attempt timeout. 5xx/408/429 and transport errors retry
            // with exponential backoff; 4xx client errors do not (a poison
            // message must not be hammered — it parks and shows on /health/ready).
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
                resilience.Retry.MaxRetryAttempts = 3;
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(500);
                resilience.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                resilience.Retry.UseJitter = true;
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                resilience.CircuitBreaker.MinimumThroughput = 5;
                resilience.CircuitBreaker.FailureRatio = 0.5;
                resilience.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(10);
            });

        services.AddHostedService<OutboxPublisher>();
        services.AddHealthChecks().AddCheck<OutboxHealthCheck>("outbox", tags: ["ready"]);

        return services;
    }
}
