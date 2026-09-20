using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;
using TalentBridge.Search.Api.Configuration;
using TalentBridge.Search.Api.Jobs;
using TalentBridge.Search.Infrastructure.Projections;

namespace TalentBridge.Search.Api.Projections;

/// <summary>
/// The only write path into the read model. Internal: not for browsers, not
/// documented publicly, guarded by a shared secret the Post API's outbox
/// publisher sends on every push.
/// </summary>
public static class ProjectionEndpoints
{
    public static IEndpointRouteBuilder MapProjectionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/internal/projections")
            .WithTags("Internal")
            .AddEndpointFilter<SharedSecretFilter>()
            .ExcludeFromDescription()
            .MapPost("/job", ApplyJob);

        return app;
    }

    private static async Task<Results<Accepted<ProjectionReceipt>, ValidationProblem>> ApplyJob(
        JobProjectionMessage message,
        JobProjectionHandler handler,
        IOutputCacheStore cache,
        ILogger<JobProjectionHandler> logger,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (message.Id == Guid.Empty)
        {
            errors["id"] = ["Id is required."];
        }

        if (message.Version < 1)
        {
            errors["version"] = ["Version must be at least 1."];
        }

        if (string.IsNullOrWhiteSpace(message.Slug))
        {
            errors["slug"] = ["Slug is required."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var applied = await handler.ApplyAsync(message, ct);
        if (applied)
        {
            // Lists, facets and every detail page may now be stale.
            await cache.EvictByTagAsync(JobEndpoints.CacheTag, ct);
        }

        logger.LogInformation("Projection for {JobId} v{Version} {Outcome}", message.Id, message.Version, applied ? "applied" : "ignored (stale or duplicate)");

        // 202 either way: a stale or duplicate message is a successful no-op and
        // the publisher must mark it processed rather than retry it.
        return TypedResults.Accepted((string?)null, new ProjectionReceipt(message.Id, message.Version, applied));
    }

    public sealed record ProjectionReceipt(Guid Id, int Version, bool Applied);

    /// <summary>Constant-time comparison of the shared secret header; 401 on any mismatch.</summary>
    private sealed class SharedSecretFilter(IOptions<ProjectionOptions> options) : IEndpointFilter
    {
        private readonly byte[] _expected = Encoding.UTF8.GetBytes(options.Value.SharedSecret);

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var presented = context.HttpContext.Request.Headers[ProjectionOptions.HeaderName].ToString();
            var bytes = Encoding.UTF8.GetBytes(presented);
            if (bytes.Length == 0 || !CryptographicOperations.FixedTimeEquals(bytes, _expected))
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Not authorized.", detail: "A valid projection secret is required.");
            }

            return await next(context);
        }
    }
}
