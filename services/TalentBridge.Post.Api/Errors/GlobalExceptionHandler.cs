using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace TalentBridge.Post.Api.Errors;

/// <summary>
/// Last line of defence: nothing but a ProblemDetails body ever reaches a client.
/// Malformed requests (bad JSON, missing body) become 400; everything else is a
/// 500 with the trace id so the log line can be found.
/// </summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            BadHttpRequestException bad => (bad.StatusCode, "The request could not be read.", SafeDetail(bad)),
            System.Text.Json.JsonException => (StatusCodes.Status400BadRequest, "The request body is not valid JSON.", null),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }

    // BadHttpRequestException messages are framework-authored and safe; a
    // JsonException wrapped inside is not (it can echo request fragments).
    private static string? SafeDetail(BadHttpRequestException ex) =>
        ex.InnerException is System.Text.Json.JsonException ? "The request body is not valid JSON." : ex.Message;
}
