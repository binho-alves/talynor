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
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequestSizeLimit(8 * 1024)]
    [RegistrationProblemDetails] // Reuses the existing endpoint-only CSRF/input sanitization.
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Login(LoginRequest request, [FromServices] LoginUser login,
        [FromServices] ILogger<AuthController> logger, CancellationToken cancellationToken)
    {
        // Also rejects an authenticated but ambiguous principal, rather than switching accounts.
        if (User.Identities.Any(identity => identity.IsAuthenticated))
            return RegistrationProblem(StatusCodes.Status409Conflict, "already_authenticated",
                "Encerre a sessão atual antes de autenticar outra conta.");
        try
        {
            return await login.ExecuteAsync(request.Email!, request.Password!, cancellationToken) switch
            {
                LoginUserResult.Success => NoContent(),
                LoginUserResult.AlreadyAuthenticated => RegistrationProblem(StatusCodes.Status409Conflict,
                    "already_authenticated", "Encerre a sessão atual antes de autenticar outra conta."),
                LoginUserResult.InvalidCredentials => RegistrationProblem(StatusCodes.Status401Unauthorized,
                    "invalid_credentials", "Email ou senha inválidos."),
                _ => throw new InvalidOperationException("Unexpected login result.")
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Session login failed due to an unexpected operational error.");
            return RegistrationProblem(StatusCodes.Status500InternalServerError, "unexpected_error",
                "Não foi possível concluir a operação.");
        }
    }

    [HttpGet("me")]
    [Authorize]
    [RegistrationProblemDetails]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Me([FromServices] GetCurrentUser current,
        [FromServices] ISessionService sessions, [FromServices] ILogger<AuthController> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var account = await current.ExecuteAsync(cancellationToken);
            if (account is not null) return Ok(new CurrentUserResponse(account.Id, account.Email));
            await sessions.LogoutAsync(cancellationToken);
            return RegistrationProblem(StatusCodes.Status401Unauthorized, "authentication_required",
                "Autenticação necessária.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Current account lookup failed due to an unexpected operational error.");
            return RegistrationProblem(StatusCodes.Status500InternalServerError, "unexpected_error",
                "Não foi possível concluir a operação.");
        }
    }

    [HttpPost("logout")]
    [Authorize]
    [RegistrationProblemDetails]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Logout([FromServices] ISessionService sessions,
        [FromServices] ILogger<AuthController> logger, CancellationToken cancellationToken)
    {
        try
        {
            await sessions.LogoutAsync(cancellationToken);
            return NoContent();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Session logout failed due to an unexpected operational error.");
            return RegistrationProblem(StatusCodes.Status500InternalServerError, "unexpected_error",
                "Não foi possível concluir a operação.");
        }
    }

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
