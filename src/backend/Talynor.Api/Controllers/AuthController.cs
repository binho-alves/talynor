using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Talynor.Api.Contracts;
using Talynor.Api.Filters;
using Talynor.Application.Accounts;

namespace Talynor.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AutoValidateAntiforgeryToken]
public sealed class AuthController(IAntiforgery antiforgery) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("registration")]
    [RequestSizeLimit(8 * 1024)]
    [RegistrationProblemDetails]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Register(
        RegisterRequest request, [FromServices] RegisterUser register,
        [FromServices] ILogger<AuthController> logger, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RegistrationProblem(StatusCodes.Status409Conflict, "already_authenticated",
                "Encerre a sessão atual antes de criar outra conta.");
        }

        try
        {
            var result = await register.ExecuteAsync(request.Email!, request.Password!, cancellationToken);
            return result.Status switch
            {
                RegisterUserStatus.Success => StatusCode(StatusCodes.Status201Created),
                RegisterUserStatus.Duplicate => RegistrationProblem(StatusCodes.Status400BadRequest,
                    "registration_failed", "Não foi possível criar a conta com os dados informados."),
                RegisterUserStatus.Invalid => new BadRequestObjectResult(new ValidationProblemDetails(
                    result.Errors!.ToDictionary(entry => entry.Key, entry => entry.Value))
                {
                    Status = StatusCodes.Status400BadRequest, Title = "Confira os campos informados.",
                    Extensions = { ["code"] = "validation_failed" }
                }),
                _ => throw new InvalidOperationException("Unexpected registration result.")
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Do not log the exception: provider/Identity diagnostics may contain submitted data.
            logger.LogError("Account registration failed due to an unexpected operational error.");
            return RegistrationProblem(StatusCodes.Status500InternalServerError, "unexpected_error",
                "Não foi possível concluir a operação.");
        }
    }

    private static ObjectResult RegistrationProblem(int status, string code, string title) => new(
        new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } })
    { StatusCode = status };

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
