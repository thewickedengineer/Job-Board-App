using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TalentBridge.Post.Api.Configuration;
using TalentBridge.Post.Api.Outbox;
using TalentBridge.Post.Domain.JobPostings;
using TalentBridge.Post.Domain.Managers;
using TalentBridge.Post.Domain.Outbox;
using TalentBridge.Post.Infrastructure.Persistence;

namespace TalentBridge.Post.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class OutboxPublisherTests(PostgresFixture postgres) : IDisposable
{
    private readonly PostApiFactory _factory = new(postgres.ConnectionString);

    private static readonly OutboxOptions Options = new() { BatchSize = 20, MaxAttempts = 3, BackedUpAfterMinutes = 2 };

    private PostDbContext NewDb() => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<PostDbContext>();

    /// <summary>A publisher whose HTTP goes to an in-memory handler instead of the network.</summary>
    private OutboxPublisher NewPublisher(FakeSearchApi search) => new(
        _factory.Services.GetRequiredService<IServiceScopeFactory>(),
        new ProjectionClient(new HttpClient(search) { BaseAddress = new Uri("http://search-api.test") }),
        Microsoft.Extensions.Options.Options.Create(Options),
        TimeProvider.System,
        NullLogger<OutboxPublisher>.Instance);

    private static JobPostingContent Content(string title) => new(
        ReferenceCode: null, Title: title, Department: "Operations", EmploymentType: "FullTime", Seniority: "Senior",
        Openings: 1, WorkArrangement: "Hybrid", Location: "Leeds", Country: "United Kingdom",
        SalaryMin: 38_000, SalaryMax: 46_000, SalaryCurrency: "GBP", PayPeriod: "Annual", SalaryVisible: true,
        Description: new string('d', 60), Responsibilities: null, Requirements: null, Skills: ["WMS"],
        ApplicationUrl: null, ApplicationEmail: null, ClosingDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10));

    /// <summary>Writes a manager plus a published posting and its outbox row, exactly as the endpoint does.</summary>
    private async Task<(JobPosting Posting, OutboxMessage Message)> SeedPublishedPostingAsync(string slug)
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var manager = Manager.Register($"m.{Guid.NewGuid():N}@northline.co", "M", "Northline", now);
        manager.SetPasswordHash("x");
        var posting = JobPosting.Create(manager.Id, Content("Seeded"), slug, publish: true, now);
        var message = OutboxMessage.Create(posting.Id, JobProjectionMessage.MessageType,
            JsonSerializer.Serialize(JobProjectionMessage.From(posting, manager.Organization), JsonSerializerOptions.Web), now);

        db.Managers.Add(manager);
        db.JobPostings.Add(posting);
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        return (posting, message);
    }

    [Fact]
    public async Task Message_is_written_in_the_same_transaction_as_the_posting()
    {
        // Positive half: one SaveChanges, both rows land.
        var (posting, message) = await SeedPublishedPostingAsync($"tx-{Guid.NewGuid():N}");
        await using (var db = NewDb())
        {
            Assert.True(await db.JobPostings.AnyAsync(p => p.Id == posting.Id));
            Assert.True(await db.OutboxMessages.AnyAsync(m => m.Id == message.Id));
        }

        // Negative half: the posting insert fails (duplicate slug) → the outbox row
        // written in the same change set must not survive either.
        await using (var db = NewDb())
        {
            var now = DateTimeOffset.UtcNow;
            var manager = Manager.Register($"m.{Guid.NewGuid():N}@northline.co", "M", "Northline", now);
            manager.SetPasswordHash("x");
            var duplicate = JobPosting.Create(manager.Id, Content("Duplicate slug"), posting.Slug, publish: true, now);
            var orphan = OutboxMessage.Create(duplicate.Id, JobProjectionMessage.MessageType, "{}", now);
            db.Managers.Add(manager);
            db.JobPostings.Add(duplicate);
            db.OutboxMessages.Add(orphan);

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var db = NewDb())
        {
            Assert.Equal(1, await db.JobPostings.CountAsync(p => p.Slug == posting.Slug));
            Assert.False(await db.OutboxMessages.AnyAsync(m => m.AggregateId != posting.Id && m.Payload == "{}"));
        }
    }

    [Fact]
    public async Task Failed_push_leaves_the_message_unprocessed_and_increments_attempts()
    {
        var (_, message) = await SeedPublishedPostingAsync($"fail-{Guid.NewGuid():N}");
        var search = new FakeSearchApi(HttpStatusCode.InternalServerError, "boom");

        var claimed = await NewPublisher(search).ProcessBatchAsync(CancellationToken.None);

        Assert.True(claimed >= 1);
        await using var db = NewDb();
        var stored = await db.OutboxMessages.SingleAsync(m => m.Id == message.Id);
        Assert.Null(stored.ProcessedAt);
        Assert.Equal(1, stored.Attempts);
        Assert.Contains("HTTP 500", stored.LastError);
        Assert.Contains("boom", stored.LastError);
    }

    [Fact]
    public async Task Successful_push_marks_the_message_processed_and_sends_the_payload()
    {
        var (posting, message) = await SeedPublishedPostingAsync($"ok-{Guid.NewGuid():N}");
        var search = new FakeSearchApi(HttpStatusCode.Accepted, "{}");

        await NewPublisher(search).ProcessBatchAsync(CancellationToken.None);

        await using var db = NewDb();
        var stored = await db.OutboxMessages.SingleAsync(m => m.Id == message.Id);
        Assert.NotNull(stored.ProcessedAt);
        Assert.Null(stored.LastError);

        var sent = Assert.Single(search.Received, r => r.Body.Contains(posting.Id.ToString()));
        Assert.Equal(ProjectionClient.Route, sent.Path);
        Assert.Equal(message.Id.ToString(), sent.MessageIdHeader);
        var payload = JsonSerializer.Deserialize<JobProjectionMessage>(sent.Body, JsonSerializerOptions.Web)!;
        Assert.Equal(posting.Slug, payload.Slug);
        Assert.Equal(1, payload.Version);
    }

    [Fact]
    public async Task Parked_messages_are_skipped_and_reported_unhealthy()
    {
        var (_, message) = await SeedPublishedPostingAsync($"park-{Guid.NewGuid():N}");
        var search = new FakeSearchApi(HttpStatusCode.BadGateway, "down");
        var publisher = NewPublisher(search);

        for (var i = 0; i < Options.MaxAttempts; i++)
        {
            await publisher.ProcessBatchAsync(CancellationToken.None);
        }

        var deliveriesBefore = search.Received.Count(r => r.Body.Contains(message.AggregateId.ToString()));
        await publisher.ProcessBatchAsync(CancellationToken.None);
        var deliveriesAfter = search.Received.Count(r => r.Body.Contains(message.AggregateId.ToString()));

        Assert.Equal(Options.MaxAttempts, deliveriesBefore);
        Assert.Equal(deliveriesBefore, deliveriesAfter); // parked: no further attempts

        await using var db = NewDb();
        var stored = await db.OutboxMessages.SingleAsync(m => m.Id == message.Id);
        Assert.Null(stored.ProcessedAt);
        Assert.Equal(Options.MaxAttempts, stored.Attempts);

        var health = await new OutboxHealthCheck(db, Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System)
            .CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, health.Status);
        Assert.True((int)health.Data["parked"] >= 1);
    }

    public void Dispose() => _factory.Dispose();

    /// <summary>Stands in for the Search API: records every push and answers with a fixed status.</summary>
    private sealed class FakeSearchApi(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<(string Path, string Body, string? MessageIdHeader)> Received { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = await request.Content!.ReadAsStringAsync(cancellationToken);
            request.Headers.TryGetValues("X-Outbox-Message-Id", out var ids);
            lock (Received)
            {
                Received.Add((request.RequestUri!.AbsolutePath, content, ids?.FirstOrDefault()));
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
