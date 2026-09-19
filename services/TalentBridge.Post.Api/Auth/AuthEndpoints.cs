using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TalentBridge.Post.Api.Configuration;
using TalentBridge.Post.Api.Validation;
using TalentBridge.Post.Domain.Managers;
using TalentBridge.Post.Infrastructure.Persistence;

namespace TalentBridge.Post.Api.Auth;

public static class AuthEndpoints
{
    private const string GenericLoginFailure = "We couldn't match that email and password.";

    // Verified against when the email is unknown so a missing account costs the
    // same time as a wrong password. PasswordHasher ignores the user argument.
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<Manager>().HashPassword(null!, Guid.NewGuid().ToString("N")));

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapPost("/signup", Signup)
            .WithValidation<SignupRequest>()
            .RequireRateLimiting(AuthRateLimitOptions.SignupPolicy)
            .ProducesValidationProblem()
            .WithSummary("Create a hiring manager account and start a session.");

        auth.MapPost("/login", Login)
            .WithValidation<LoginRequest>()
            .RequireRateLimiting(AuthRateLimitOptions.LoginPolicy)
            .ProducesValidationProblem()
            .WithSummary("Exchange credentials for an access token and a refresh cookie.");

        auth.MapPost("/refresh", Refresh)
            .WithSummary("Rotate the refresh token and issue a new access token.");

        auth.MapPost("/logout", Logout)
            .WithSummary("Revoke the presented refresh token and clear the cookie.");

        app.MapGet("/api/me", Me)
            .RequireAuthorization()
            .WithTags("Auth")
            .WithSummary("The authenticated manager's profile.");

        return app;
    }

    private static async Task<Results<Created<AuthResponse>, ValidationProblem>> Signup(
        SignupRequest request,
        PostDbContext db,
        IPasswordHasher<Manager> hasher,
        TokenService tokens,
        TimeProvider clock,
        HttpContext http,
        CancellationToken ct)
    {
        var email = request.Email.Trim();
        if (await db.Managers.AnyAsync(m => m.Email == email, ct))
        {
            return EmailTaken();
        }

        var now = clock.GetUtcNow();
        var manager = Manager.Register(email, request.FullName, request.Organization, now);
        manager.SetPasswordHash(hasher.HashPassword(manager, request.Password));
        manager.RecordLogin(now);

        var (rawRefresh, refreshHash) = TokenService.CreateRefreshToken();
        var refresh = manager.IssueRefreshToken(refreshHash, now, tokens.RefreshTokenLifetime);

        db.Managers.Add(manager);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost a race with a concurrent signup for the same email.
            return EmailTaken();
        }

        RefreshTokenCookie.Set(http.Response, rawRefresh, refresh.ExpiresAt);
        var response = new AuthResponse(tokens.CreateAccessToken(manager), rawRefresh, ManagerResponse.From(manager));
        return TypedResults.Created("/api/me", response);
    }

    private static async Task<Results<Ok<AuthResponse>, ProblemHttpResult>> Login(
        LoginRequest request,
        PostDbContext db,
        IPasswordHasher<Manager> hasher,
        TokenService tokens,
        TimeProvider clock,
        HttpContext http,
        CancellationToken ct)
    {
        var email = request.Email.Trim();
        var manager = await db.Managers.SingleOrDefaultAsync(m => m.Email == email, ct);

        var verification = manager is null
            ? hasher.VerifyHashedPassword(null!, DummyHash.Value, request.Password)
            : hasher.VerifyHashedPassword(manager, manager.PasswordHash, request.Password);

        if (manager is null || verification == PasswordVerificationResult.Failed)
        {
            return Unauthorized(GenericLoginFailure);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            manager.SetPasswordHash(hasher.HashPassword(manager, request.Password));
        }

        var now = clock.GetUtcNow();
        manager.RecordLogin(now);

        var (rawRefresh, refreshHash) = TokenService.CreateRefreshToken();
        var refresh = manager.IssueRefreshToken(refreshHash, now, tokens.RefreshTokenLifetime);
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(ct);

        RefreshTokenCookie.Set(http.Response, rawRefresh, refresh.ExpiresAt);
        return TypedResults.Ok(new AuthResponse(tokens.CreateAccessToken(manager), rawRefresh, ManagerResponse.From(manager)));
    }

    private static async Task<Results<Ok<TokenPairResponse>, ProblemHttpResult>> Refresh(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RefreshRequest? body,
        PostDbContext db,
        TokenService tokens,
        TimeProvider clock,
        HttpContext http,
        CancellationToken ct)
    {
        var raw = RefreshTokenCookie.Read(http.Request) ?? body?.RefreshToken;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Unauthorized("Refresh token missing.");
        }

        var now = clock.GetUtcNow();
        var hash = TokenService.HashRefreshToken(raw);
        var presented = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (presented is null)
        {
            RefreshTokenCookie.Clear(http.Response);
            return Unauthorized("Refresh token is not valid.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Rotation is a conditional update so two concurrent refreshes cannot
        // both succeed. A token that is already revoked means the old value was
        // replayed — treat the whole family as compromised.
        var rotated = presented.IsActive(now) && await db.RefreshTokens
            .Where(t => t.Id == presented.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct) == 1;

        if (!rotated)
        {
            if (presented.RevokedAt is not null)
            {
                await db.RefreshTokens
                    .Where(t => t.ManagerId == presented.ManagerId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
                await tx.CommitAsync(ct);
            }

            RefreshTokenCookie.Clear(http.Response);
            return Unauthorized("Session has expired. Please log in again.");
        }

        var manager = await db.Managers.SingleAsync(m => m.Id == presented.ManagerId, ct);
        var (rawRefresh, refreshHash) = TokenService.CreateRefreshToken();
        var refresh = manager.IssueRefreshToken(refreshHash, now, tokens.RefreshTokenLifetime);
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        RefreshTokenCookie.Set(http.Response, rawRefresh, refresh.ExpiresAt);
        return TypedResults.Ok(new TokenPairResponse(tokens.CreateAccessToken(manager), rawRefresh));
    }

    private static async Task<NoContent> Logout(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RefreshRequest? body,
        PostDbContext db,
        TimeProvider clock,
        HttpContext http,
        CancellationToken ct)
    {
        var raw = RefreshTokenCookie.Read(http.Request) ?? body?.RefreshToken;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var hash = TokenService.HashRefreshToken(raw);
            var now = clock.GetUtcNow();
            await db.RefreshTokens
                .Where(t => t.TokenHash == hash && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        }

        RefreshTokenCookie.Clear(http.Response);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ManagerResponse>, UnauthorizedHttpResult>> Me(
        HttpContext http,
        PostDbContext db,
        CancellationToken ct)
    {
        var id = TokenService.ManagerIdFrom(http.User);
        var manager = id is null ? null : await db.Managers.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct);
        return manager is null ? TypedResults.Unauthorized() : TypedResults.Ok(ManagerResponse.From(manager));
    }

    private static ValidationProblem EmailTaken() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["email"] = ["An account with this email already exists."],
        });

    private static ProblemHttpResult Unauthorized(string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Not authenticated.", detail: detail);
}
