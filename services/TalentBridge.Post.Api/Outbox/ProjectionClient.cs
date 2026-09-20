using System.Net.Http.Headers;
using System.Text;
using TalentBridge.Post.Domain.Outbox;

namespace TalentBridge.Post.Api.Outbox;

/// <summary>
/// The one HTTP call the write side makes: push a projection message to the
/// Search API. The typed client carries the base address and shared secret; the
/// standard resilience handler (retry, breaker, timeouts) is attached in DI.
/// </summary>
public sealed class ProjectionClient(HttpClient http)
{
    public const string Route = "/internal/projections/job";

    /// <summary>
    /// Returns null on success. On failure returns a short description for
    /// <c>last_error</c>; the caller decides whether that counts as an attempt.
    /// </summary>
    public async Task<ProjectionOutcome> PushAsync(OutboxMessage message, CancellationToken ct)
    {
        using var content = new StringContent(message.Payload, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        using var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = content };
        request.Headers.TryAddWithoutValidation("X-Outbox-Message-Id", message.Id.ToString());

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return ProjectionOutcome.Delivered;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            return ProjectionOutcome.Failed($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body)}");
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException)
        {
            // The Search API is known to be down; nothing was sent, so this is
            // not a delivery attempt. The row is retried on a later poll.
            return ProjectionOutcome.Skipped;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            return ProjectionOutcome.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}

public readonly record struct ProjectionOutcome(ProjectionStatus Status, string? Error)
{
    public static readonly ProjectionOutcome Delivered = new(ProjectionStatus.Delivered, null);
    public static readonly ProjectionOutcome Skipped = new(ProjectionStatus.Skipped, null);
    public static ProjectionOutcome Failed(string error) => new(ProjectionStatus.Failed, error);
}

public enum ProjectionStatus
{
    Delivered,
    Failed,
    Skipped,
}
