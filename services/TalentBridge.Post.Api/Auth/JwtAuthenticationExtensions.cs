using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TalentBridge.Post.Api.Configuration;
using TalentBridge.Post.Domain.Managers;

namespace TalentBridge.Post.Api.Auth;

public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddPostAuthentication(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TokenService>();
        services.AddSingleton<IPasswordHasher<Manager>, PasswordHasher<Manager>>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Options are resolved lazily so validation of JwtOptions runs first and
        // a missing secret fails at startup with a clear message, not an obscure
        // "IDX10..." at first request.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = TokenService.SigningKey(jwt.Value),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Name,
                    RoleClaimType = ClaimTypes.Role,
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<AuthRateLimitOptions>()
            .BindConfiguration(AuthRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.AddPolicy(AuthRateLimitOptions.LoginPolicy, ctx =>
            {
                var o = ctx.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;
                return PerIp(ctx, o.LoginPermitLimit, TimeSpan.FromMinutes(o.LoginWindowMinutes));
            });

            limiter.AddPolicy(AuthRateLimitOptions.SignupPolicy, ctx =>
            {
                var o = ctx.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;
                return PerIp(ctx, o.SignupPermitLimit, TimeSpan.FromMinutes(o.SignupWindowMinutes));
            });

            limiter.OnRejected = async (ctx, ct) =>
            {
                var retryAfter = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
                    ? (int)Math.Ceiling(wait.TotalSeconds)
                    : 60;

                ctx.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();

                var problemDetails = ctx.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetails.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = ctx.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Type = "https://tools.ietf.org/html/rfc6585#section-4",
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many attempts.",
                        Detail = $"Try again in {retryAfter} seconds.",
                        Extensions = { ["retryAfterSeconds"] = retryAfter },
                    },
                });
            };
        });

        return services;
    }

    private static RateLimitPartition<string> PerIp(HttpContext ctx, int permitLimit, TimeSpan window)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }
}
