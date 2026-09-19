namespace TalentBridge.Post.Api.Auth;

/// <summary>
/// The browser never sees the refresh token in script: it travels only in an
/// httpOnly, SameSite=Strict cookie scoped to the auth endpoints.
/// </summary>
public static class RefreshTokenCookie
{
    public const string Name = "tb_refresh";
    private const string Path = "/api/auth";

    public static void Set(HttpResponse response, string rawToken, DateTimeOffset expires) =>
        response.Cookies.Append(Name, rawToken, Options(response.HttpContext, expires));

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, Options(response.HttpContext, expires: null));

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static CookieOptions Options(HttpContext ctx, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        // Secure on HTTPS; plain-http localhost still gets the cookie in dev.
        Secure = ctx.Request.IsHttps,
        Path = Path,
        Expires = expires,
        IsEssential = true,
    };
}
