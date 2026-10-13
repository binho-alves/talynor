using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Talynor.Api.Contracts;

namespace Talynor.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AutoValidateAntiforgeryToken]
public sealed class AuthController(IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    [AllowAnonymous]
    [ProducesResponseType<CsrfTokenResponse>(StatusCodes.Status200OK)]
    public ActionResult<CsrfTokenResponse> GetCsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers.CacheControl = "no-store";
        var token = tokens.RequestToken
            ?? throw new InvalidOperationException("Antiforgery did not produce a request token.");
        return Ok(new CsrfTokenResponse(token));
    }
}
