using System.Security.Claims;
using NotificationService.Api.Features.Auth;

namespace NotificationService.Api.Common;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>The calling client. Endpoints that call this require a key, so the claim is always present.</summary>
    public static Guid ClientId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ApiKeyAuthentication.ClientIdClaim)
            ?? throw new InvalidOperationException("The principal has no client id claim."));
}
