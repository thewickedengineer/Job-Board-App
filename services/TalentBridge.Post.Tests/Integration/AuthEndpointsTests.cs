using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TalentBridge.Post.Api.Auth;

namespace TalentBridge.Post.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class AuthEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private const string Password = "CorrectHorse42";
    private readonly PostApiFactory _factory = new(postgres.ConnectionString);

    private static string UniqueEmail() => $"dana.{Guid.NewGuid():N}@northline.co";

    private static SignupRequest Signup(string? email = null) =>
        new(email ?? UniqueEmail(), Password, "Dana Whitfield", "Northline");

    [Fact]
    public async Task Signup_returns_201_with_tokens_profile_and_refresh_cookie()
    {
        using var client = _factory.CreateClient();
        var request = Signup();

        var response = await client.PostAsJsonAsync("/api/auth/signup", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/me", response.Headers.Location?.ToString());

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(body);
        Assert.NotEmpty(body.AccessToken);
        Assert.NotEmpty(body.RefreshToken);
        Assert.Equal(request.Email, body.Manager.Email);
        Assert.Equal("Northline", body.Manager.Organization);
        Assert.False(body.Manager.EmailVerified);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{RefreshTokenCookie.Name}=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Signup_with_invalid_payload_returns_400_with_camelCase_keys()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/signup",
            new SignupRequest("dana@gmail.com", "short", "D", "N"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("One or more validation errors occurred.", problem.GetProperty("title").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));

        var errors = problem.GetProperty("errors");
        Assert.Equal(["email", "password", "fullName", "organization"], errors.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Contains("personal mailbox", errors.GetProperty("email")[0].GetString());
    }

    [Fact]
    public async Task Signup_with_duplicate_email_is_case_insensitive_and_keyed_to_email()
    {
        using var client = _factory.CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/api/auth/signup", Signup(email));

        var response = await client.PostAsJsonAsync("/api/auth/signup", Signup(email.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var emailErrors = problem.GetProperty("errors").GetProperty("email");
        Assert.Equal("An account with this email already exists.", emailErrors[0].GetString());
    }

    [Fact]
    public async Task Login_failure_message_is_identical_for_wrong_password_and_unknown_email()
    {
        using var client = _factory.CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/api/auth/signup", Signup(email));

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "wrong-password"));
        var unknownEmail = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(UniqueEmail(), "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        var a = await wrongPassword.Content.ReadFromJsonAsync<JsonElement>();
        var b = await unknownEmail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(a.GetProperty("detail").GetString(), b.GetProperty("detail").GetString());
        Assert.DoesNotContain("Set-Cookie", wrongPassword.Headers.Select(h => h.Key));
    }

    [Fact]
    public async Task Login_accepts_any_email_casing_and_me_returns_the_profile()
    {
        using var client = _factory.CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/api/auth/signup", Signup(email));

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email.ToUpperInvariant(), Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokens = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(tokens);

        var anonymous = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var me = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var profile = await me.Content.ReadFromJsonAsync<ManagerResponse>();
        Assert.Equal(email, profile!.Email);
        Assert.NotNull(profile.LastLoginAt);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_a_replayed_token_revokes_the_family()
    {
        // HandleCookies is on by default, so the refresh cookie round-trips.
        using var client = _factory.CreateClient();
        var signup = await client.PostAsJsonAsync("/api/auth/signup", Signup());
        var first = (await signup.Content.ReadFromJsonAsync<AuthResponse>())!;

        var refresh = await client.PostAsync("/api/auth/refresh", content: null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var second = (await refresh.Content.ReadFromJsonAsync<TokenPairResponse>())!;
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotEmpty(second.AccessToken);

        // Replay the old value via the body form (a stolen token from a non-browser client).
        using var noCookies = _factory.CreateClient(new() { HandleCookies = false });
        var replay = await noCookies.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(first.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // The current token is collateral: the whole family is revoked.
        var current = await noCookies.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(second.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, current.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_clears_the_cookie()
    {
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/signup", Signup());

        var logout = await client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var cleared = Assert.Single(logout.Headers.GetValues("Set-Cookie"));
        Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);

        var refresh = await client.PostAsync("/api/auth/refresh", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Login_is_rate_limited_per_ip_with_retry_after()
    {
        using var limited = new PostApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["AuthRateLimit:LoginPermitLimit"] = "2",
            ["AuthRateLimit:LoginWindowMinutes"] = "1",
        });
        using var client = limited.CreateClient();
        var attempt = new LoginRequest(UniqueEmail(), "wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", attempt)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", attempt)).StatusCode);

        var third = await client.PostAsJsonAsync("/api/auth/login", attempt);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.NotNull(third.Headers.RetryAfter?.Delta);
        var problem = await third.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(429, problem.GetProperty("status").GetInt32());
        Assert.True(problem.GetProperty("retryAfterSeconds").GetInt32() > 0);
    }

    public void Dispose() => _factory.Dispose();
}
