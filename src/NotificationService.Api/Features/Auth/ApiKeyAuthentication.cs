using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotificationService.Core.Clients;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.Api.Features.Auth;

/// <summary>Callers identify themselves with <c>X-Api-Key</c>; each key belongs to one client and its role.</summary>
internal static class ApiKeyAuthentication
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string ClientIdClaim = "client_id";
    public const string NameClaim = "client_name";
    public const string RoleClaim = "role";
    public const string AdminPolicy = "Admin";

    public static IServiceCollection AddApiKeyAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(Scheme, configureOptions: null);
        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(Scheme).RequireAuthenticatedUser().Build())
            .AddPolicy(AdminPolicy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .RequireRole(nameof(ClientRole.Admin)));
        return services;
    }
}

internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    NotificationsDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthentication.HeaderName, out var values) || values.Count == 0)
        {
            return AuthenticateResult.NoResult();
        }

        var key = values.ToString();
        var prefix = ApiKey.PrefixOf(key);
        if (prefix is null)
        {
            return AuthenticateResult.Fail("The API key is malformed.");
        }

        var client = await db.ApiClients.AsNoTracking().SingleOrDefaultAsync(c => c.KeyPrefix == prefix, Context.RequestAborted);
        if (client is null || !client.Accepts(key))
        {
            return AuthenticateResult.Fail("The API key is unknown or revoked.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ApiKeyAuthentication.ClientIdClaim, client.Id.ToString()),
                new Claim(ApiKeyAuthentication.NameClaim, client.Name),
                new Claim(ApiKeyAuthentication.RoleClaim, client.Role.ToString()),
            ],
            ApiKeyAuthentication.Scheme, ApiKeyAuthentication.NameClaim, ApiKeyAuthentication.RoleClaim);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyAuthentication.Scheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"{ApiKeyAuthentication.Scheme} header=\"{ApiKeyAuthentication.HeaderName}\"";
        return Task.CompletedTask;
    }
}
