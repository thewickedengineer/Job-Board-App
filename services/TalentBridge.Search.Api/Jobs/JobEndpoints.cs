using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using TalentBridge.Search.Infrastructure.Queries;

namespace TalentBridge.Search.Api.Jobs;

/// <summary>
/// The public, anonymous read API. Every response here is cacheable by
/// anyone in the path — the output cache in-process, the browser via
/// Cache-Control, and repeat detail views via ETag/304.
/// </summary>
public static class JobEndpoints
{
    public const string CacheTag = "jobs";
    public const string ListPolicy = "jobs-list";
    public const string DetailPolicy = "job-detail";

    public static readonly TimeSpan ListTtl = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DetailTtl = TimeSpan.FromSeconds(300);

    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var jobs = app.MapGroup("/api").WithTags("Jobs");

        jobs.MapGet("/jobs", List).CacheOutput(ListPolicy).ProducesValidationProblem()
            .WithSummary("Open postings, filtered, sorted and paged.");
        jobs.MapGet("/jobs/{slug}", Detail).CacheOutput(DetailPolicy)
            .WithSummary("One posting by slug, with similar open roles. Supports If-None-Match.");
        jobs.MapGet("/facets", FacetsAsync).CacheOutput(ListPolicy).ProducesValidationProblem()
            .WithSummary("Filter counts for the current query.");

        return app;
    }

    private static async Task<Results<Ok<PagedResponse<JobSummaryResponse>>, ValidationProblem>> List(
        [AsParameters] JobListRequest request,
        JobListQuery query,
        HttpResponse response,
        CancellationToken ct)
    {
        var errors = Validate(request, checkPaging: true);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var sort = string.IsNullOrWhiteSpace(request.Sort) ? JobListQuery.Sorts[0] : request.Sort;
        var page = await query.RunAsync(request.ToFilter(), sort, request.Page, request.PageSize, ct);

        PublicCache(response, ListTtl);
        var totalPages = (int)Math.Ceiling(page.Total / (double)request.PageSize);
        return TypedResults.Ok(new PagedResponse<JobSummaryResponse>(
            page.Items.Select(JobSummaryResponse.From).ToList(), request.Page, request.PageSize, page.Total, totalPages));
    }

    private static async Task<Results<Ok<JobDetailResponse>, NotFound, StatusCodeHttpResult>> Detail(
        string slug,
        JobDetailQuery query,
        HttpRequest request,
        HttpResponse response,
        CancellationToken ct)
    {
        var detail = await query.RunAsync(slug, ct);
        if (detail is null)
        {
            return TypedResults.NotFound();
        }

        // Cheap, deterministic validator: it changes whenever this row or any of
        // the similar rows is re-projected. No need to hash the whole body.
        var etag = ETagFor(detail);
        response.Headers.ETag = etag.ToString();
        PublicCache(response, DetailTtl);

        if (request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var ifNoneMatch)
            && EntityTagHeaderValue.TryParseList(ifNoneMatch, out var candidates)
            && candidates.Any(c => c.Compare(etag, useStrongComparison: false)))
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        return TypedResults.Ok(JobDetailResponse.From(detail));
    }

    private static async Task<Results<Ok<FacetsResponse>, ValidationProblem>> FacetsAsync(
        [AsParameters] JobListRequest request,
        FacetsQuery query,
        HttpResponse response,
        CancellationToken ct)
    {
        var errors = Validate(request, checkPaging: false);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var facets = await query.RunAsync(request.ToFilter(), ct);
        PublicCache(response, ListTtl);
        return TypedResults.Ok(FacetsResponse.From(facets));
    }

    private static Dictionary<string, string[]> Validate(JobListRequest r, bool checkPaging)
    {
        var errors = new Dictionary<string, string[]>();

        if (!string.IsNullOrWhiteSpace(r.Sort) && !JobListQuery.Sorts.Contains(r.Sort, StringComparer.Ordinal))
        {
            errors["sort"] = [$"Sort must be one of: {string.Join(", ", JobListQuery.Sorts)}."];
        }

        if (r.SalaryMin is < 0)
        {
            errors["salaryMin"] = ["Salary minimum must be 0 or greater."];
        }

        if (r.SalaryMax is < 0)
        {
            errors["salaryMax"] = ["Salary maximum must be 0 or greater."];
        }

        if (r.SalaryMin is not null && r.SalaryMax is not null && r.SalaryMin > r.SalaryMax)
        {
            errors["salaryMax"] = ["Salary maximum must be greater than or equal to salary minimum."];
        }

        if (r.PostedWithinDays is < 1 or > 365)
        {
            errors["postedWithinDays"] = ["Posted-within must be between 1 and 365 days."];
        }

        if (r.Q is { Length: > 200 })
        {
            errors["q"] = ["Keywords must be at most 200 characters."];
        }

        if (r.Location is { Length: > 120 })
        {
            errors["location"] = ["Location must be at most 120 characters."];
        }

        if (checkPaging)
        {
            if (r.Page < 1)
            {
                errors["page"] = ["Page must be 1 or greater."];
            }

            if (r.PageSize is < 1 or > JobListQuery.MaxPageSize)
            {
                errors["pageSize"] = [$"Page size must be between 1 and {JobListQuery.MaxPageSize}."];
            }
        }

        return errors;
    }

    private static void PublicCache(HttpResponse response, TimeSpan ttl) =>
        response.Headers.CacheControl = $"public, max-age={(int)ttl.TotalSeconds}";

    private static EntityTagHeaderValue ETagFor(JobDetail detail)
    {
        var fingerprint = new StringBuilder()
            .Append(detail.Job.Id).Append(':').Append(detail.Job.Version).Append(':').Append(detail.Job.ProjectedAt.UtcTicks)
            .Append(':').Append(detail.Job.IsOpen);
        foreach (var s in detail.Similar)
        {
            fingerprint.Append('|').Append(s.Id).Append(':').Append(s.Version);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString()));
        return new EntityTagHeaderValue($"\"{Convert.ToHexStringLower(hash.AsSpan(0, 16))}\"", isWeak: true);
    }
}
