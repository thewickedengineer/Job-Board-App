using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TalentBridge.Post.Api.Auth;
using TalentBridge.Post.Api.Validation;
using TalentBridge.Post.Domain.JobPostings;
using TalentBridge.Post.Domain.Managers;
using TalentBridge.Post.Domain.Outbox;
using TalentBridge.Post.Infrastructure.Persistence;

namespace TalentBridge.Post.Api.JobPostings;

public static class JobPostingEndpoints
{
    private const int MaxPageSize = 50;
    private const string RouteBase = "/api/job-postings";

    private static readonly string[] Sorts = ["createdDesc", "createdAsc", "closingAsc", "closingDesc", "titleAsc", "titleDesc"];
    private static readonly string[] StatusFilters = ["All", JobPostingStatus.Draft, JobPostingStatus.Published, JobPostingStatus.Closed, JobPostingStatus.Expired];

    public static IEndpointRouteBuilder MapJobPostingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RouteBase).WithTags("Job postings").RequireAuthorization();

        group.MapPost("/", Create).WithValidation<CreateJobPostingRequest>().ProducesValidationProblem()
            .WithSummary("Create a posting. Returns the complete persisted record.");
        group.MapGet("/", List).ProducesValidationProblem()
            .WithSummary("The caller's own postings, filtered, sorted and paged.");
        group.MapGet("/{id:guid}", Get).WithName("GetJobPosting");
        group.MapPut("/{id:guid}", Update).WithValidation<UpdateJobPostingRequest>().ProducesValidationProblem()
            .WithSummary("Replace the content of a posting; publishes a draft when status is Published.");
        group.MapPost("/{id:guid}/close", Close)
            .WithSummary("Close a posting. Irreversible; it leaves the public board on the next projection.");

        return app;
    }

    private static async Task<Results<Created<JobPostingResponse>, ValidationProblem, UnauthorizedHttpResult>> Create(
        CreateJobPostingRequest request,
        HttpContext http,
        PostDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var manager = await CurrentManagerAsync(http, db, ct);
        if (manager is null)
        {
            return TypedResults.Unauthorized();
        }

        var content = request.ToContent();
        if (await ReferenceCodeTakenAsync(db, manager.Id, content.ReferenceCode, exceptId: null, ct))
        {
            return ReferenceCodeTaken(content.ReferenceCode!);
        }

        var now = clock.GetUtcNow();
        var today = Today(now);
        var publish = request.Status == JobPostingStatus.Published;
        var slug = await SlugGenerator.NextAvailableAsync(db, SlugGenerator.BaseSlug(content.Title, content.Location), ct);
        var posting = JobPosting.Create(manager.Id, content, slug, publish, now);

        db.JobPostings.Add(posting);
        if (posting.IsPublished)
        {
            Enqueue(db, posting, manager.Organization, now);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "ix_job_postings_slug"))
        {
            // Two managers raced for the same slug; retry once with a random tail.
            db.Entry(posting).Property(p => p.Slug).CurrentValue = SlugGenerator.WithRandomSuffix(slug);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "ix_job_postings_manager_id_reference_code"))
        {
            return ReferenceCodeTaken(content.ReferenceCode!);
        }

        var response = JobPostingResponse.From(posting, today);
        return TypedResults.Created($"{RouteBase}/{posting.Id}", response);
    }

    private static async Task<Results<Ok<PagedResponse<JobPostingSummary>>, ValidationProblem, UnauthorizedHttpResult>> List(
        [AsParameters] JobPostingListQuery query,
        HttpContext http,
        PostDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var managerId = TokenService.ManagerIdFrom(http.User);
        if (managerId is null)
        {
            return TypedResults.Unauthorized();
        }

        var errors = new Dictionary<string, string[]>();
        var sort = string.IsNullOrWhiteSpace(query.Sort) ? Sorts[0] : query.Sort;
        if (!Sorts.Contains(sort, StringComparer.Ordinal))
        {
            errors["sort"] = [$"Sort must be one of: {string.Join(", ", Sorts)}."];
        }

        var status = string.IsNullOrWhiteSpace(query.Status) ? "All" : query.Status;
        if (!StatusFilters.Contains(status, StringComparer.Ordinal))
        {
            errors["status"] = [$"Status must be one of: {string.Join(", ", StatusFilters)}."];
        }

        if (query.Page < 1)
        {
            errors["page"] = ["Page must be 1 or greater."];
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"Page size must be between 1 and {MaxPageSize}."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var today = Today(clock.GetUtcNow());
        var postings = db.JobPostings.AsNoTracking().Where(p => p.ManagerId == managerId);

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = $"%{EscapeLike(query.Q.Trim())}%";
            postings = postings.Where(p =>
                EF.Functions.ILike(p.Title, pattern, "\\")
                || EF.Functions.ILike(p.Department, pattern, "\\")
                || (p.ReferenceCode != null && EF.Functions.ILike(p.ReferenceCode, pattern, "\\")));
        }

        postings = status switch
        {
            JobPostingStatus.Published => postings.Where(p => p.Status == JobPostingStatus.Published && p.ClosingDate >= today),
            JobPostingStatus.Expired => postings.Where(p => p.Status == JobPostingStatus.Published && p.ClosingDate < today),
            JobPostingStatus.Draft or JobPostingStatus.Closed => postings.Where(p => p.Status == status),
            _ => postings,
        };

        postings = sort switch
        {
            "createdAsc" => postings.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id),
            "closingAsc" => postings.OrderBy(p => p.ClosingDate).ThenBy(p => p.Id),
            "closingDesc" => postings.OrderByDescending(p => p.ClosingDate).ThenBy(p => p.Id),
            "titleAsc" => postings.OrderBy(p => p.Title).ThenBy(p => p.Id),
            "titleDesc" => postings.OrderByDescending(p => p.Title).ThenBy(p => p.Id),
            _ => postings.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id),
        };

        var total = await postings.CountAsync(ct);
        var items = await postings
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var totalPages = (int)Math.Ceiling(total / (double)query.PageSize);
        return TypedResults.Ok(new PagedResponse<JobPostingSummary>(
            items.Select(p => JobPostingSummary.From(p, today)).ToList(), query.Page, query.PageSize, total, totalPages));
    }

    private static async Task<Results<Ok<JobPostingResponse>, NotFound, UnauthorizedHttpResult>> Get(
        Guid id,
        HttpContext http,
        PostDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var managerId = TokenService.ManagerIdFrom(http.User);
        if (managerId is null)
        {
            return TypedResults.Unauthorized();
        }

        var posting = await db.JobPostings.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.ManagerId == managerId, ct);
        return posting is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(JobPostingResponse.From(posting, Today(clock.GetUtcNow())));
    }

    private static async Task<Results<Ok<JobPostingResponse>, NotFound, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> Update(
        Guid id,
        UpdateJobPostingRequest request,
        HttpContext http,
        PostDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var managerId = TokenService.ManagerIdFrom(http.User);
        if (managerId is null)
        {
            return TypedResults.Unauthorized();
        }

        var posting = await db.JobPostings.Include(p => p.Manager).SingleOrDefaultAsync(p => p.Id == id && p.ManagerId == managerId, ct);
        if (posting is null)
        {
            return TypedResults.NotFound();
        }

        if (posting.IsClosed)
        {
            return Conflict("This posting is closed.", "Closed postings cannot be edited. Duplicate it to post again.");
        }

        if (posting.Version != request.Version)
        {
            return Conflict("This posting changed since you loaded it.",
                $"You have version {request.Version}; the server has version {posting.Version}. Reload to see the latest.");
        }

        if (posting.IsPublished && request.Status == JobPostingStatus.Draft)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = ["A published posting cannot be reverted to a draft."],
            });
        }

        var content = request.ToContent();
        if (await ReferenceCodeTakenAsync(db, managerId.Value, content.ReferenceCode, exceptId: posting.Id, ct))
        {
            return ReferenceCodeTaken(content.ReferenceCode!);
        }

        var now = clock.GetUtcNow();
        posting.Update(content, publish: request.Status == JobPostingStatus.Published, now);
        if (posting.IsPublished)
        {
            Enqueue(db, posting, posting.Manager!.Organization, now);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("This posting changed since you loaded it.", "Another update won the race. Reload to see the latest.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "ix_job_postings_manager_id_reference_code"))
        {
            return ReferenceCodeTaken(content.ReferenceCode!);
        }

        return TypedResults.Ok(JobPostingResponse.From(posting, Today(now)));
    }

    private static async Task<Results<Ok<JobPostingResponse>, NotFound, UnauthorizedHttpResult>> Close(
        Guid id,
        HttpContext http,
        PostDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var managerId = TokenService.ManagerIdFrom(http.User);
        if (managerId is null)
        {
            return TypedResults.Unauthorized();
        }

        var posting = await db.JobPostings.Include(p => p.Manager).SingleOrDefaultAsync(p => p.Id == id && p.ManagerId == managerId, ct);
        if (posting is null)
        {
            return TypedResults.NotFound();
        }

        var now = clock.GetUtcNow();
        var wasPublished = posting.IsPublished;
        if (posting.Close(now))
        {
            // Drafts were never on the board, so only a formerly published
            // posting needs the read side told.
            if (wasPublished)
            {
                Enqueue(db, posting, posting.Manager!.Organization, now);
            }

            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(JobPostingResponse.From(posting, Today(now)));
    }

    // --- helpers ---------------------------------------------------------------

    /// <summary>
    /// Writes the outbox row that the publisher (Phase 5) will deliver. It is
    /// added to the same change set as the posting, so SaveChangesAsync commits
    /// both in one transaction or neither.
    /// </summary>
    private static void Enqueue(PostDbContext db, JobPosting posting, string organization, DateTimeOffset now)
    {
        var message = JobProjectionMessage.From(posting, organization);
        var payload = JsonSerializer.Serialize(message, JsonSerializerOptions.Web);
        db.OutboxMessages.Add(OutboxMessage.Create(posting.Id, JobProjectionMessage.MessageType, payload, now));
    }

    private static Task<Manager?> CurrentManagerAsync(HttpContext http, PostDbContext db, CancellationToken ct)
    {
        var id = TokenService.ManagerIdFrom(http.User);
        return id is null
            ? Task.FromResult<Manager?>(null)
            : db.Managers.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct);
    }

    private static async Task<bool> ReferenceCodeTakenAsync(PostDbContext db, Guid managerId, string? code, Guid? exceptId, CancellationToken ct) =>
        code is not null && await db.JobPostings.AnyAsync(
            p => p.ManagerId == managerId && p.ReferenceCode == code && (exceptId == null || p.Id != exceptId), ct);

    private static ValidationProblem ReferenceCodeTaken(string code) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["referenceCode"] = [$"{code} is already used by another of your postings."],
        });

    private static ProblemHttpResult Conflict(string title, string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: title, detail: detail);

    private static bool IsUniqueViolation(DbUpdateException ex, string constraint) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg && pg.ConstraintName == constraint;

    private static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.UtcDateTime);

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
