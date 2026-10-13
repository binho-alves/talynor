using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Talynor.Application.Accounts;

namespace Talynor.Api.Accounts;

public sealed class HttpCurrentUser(IHttpContextAccessor context) : ICurrentUser
{
    public bool IsAuthenticated => UserId is not null;

    public string? UserId
    {
        get
        {
            var principal = context.HttpContext?.User;
            if (principal is null) return null;
            var identities = principal.Identities.Where(identity => identity.IsAuthenticated).ToArray();
            if (identities.Length != 1 ||
                identities[0].AuthenticationType != IdentityConstants.ApplicationScheme) return null;
            var ids = principal.FindAll(ClaimTypes.NameIdentifier).ToArray();
            if (ids.Length != 1 || !identities[0].HasClaim(ClaimTypes.NameIdentifier, ids[0].Value) ||
                string.IsNullOrWhiteSpace(ids[0].Value)) return null;
            return ids[0].Value;
        }
    }
}
