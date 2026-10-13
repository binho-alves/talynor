using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Talynor.Api.Accounts;

namespace Talynor.IntegrationTests;

public class CurrentUserTests
{
    [Theory]
    [InlineData("no-context")]
    [InlineData("anonymous")]
    [InlineData("unexpected-type")]
    [InlineData("missing-id")]
    [InlineData("blank-id")]
    [InlineData("duplicate-id")]
    [InlineData("different-id")]
    [InlineData("multiple-identities")]
    [InlineData("anonymous-id")]
    [InlineData("valid")]
    public void Identity_is_fail_closed_without_one_unambiguous_authenticated_identifier(string scenario)
    {
        var accessor = new HttpContextAccessor();
        if (scenario != "no-context")
        {
            var identity = new ClaimsIdentity(scenario switch
            {
                "anonymous" => null,
                "unexpected-type" => "unexpected-authentication",
                _ => IdentityConstants.ApplicationScheme
            });
            if (scenario is not "missing-id" and not "anonymous-id")
                identity.AddClaim(new(ClaimTypes.NameIdentifier, scenario == "blank-id" ? " " : "id"));
            if (scenario is "duplicate-id" or "different-id")
                identity.AddClaim(new(ClaimTypes.NameIdentifier, scenario == "different-id" ? "other" : "id"));
            var principal = new ClaimsPrincipal(identity);
            if (scenario == "multiple-identities") principal.AddIdentity(new ClaimsIdentity("another-authentication"));
            if (scenario == "anonymous-id") principal.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "id")]));
            accessor.HttpContext = new DefaultHttpContext { User = principal };
        }
        var current = new HttpCurrentUser(accessor);
        Assert.Equal(scenario == "valid", current.IsAuthenticated);
        Assert.Equal(scenario == "valid" ? "id" : null, current.UserId);
    }
}
