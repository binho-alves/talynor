using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Talynor.Api.Contracts;
using Talynor.Api.Filters;
using Talynor.Application.Accounts;
using Talynor.Application.StudySessions;

namespace Talynor.Api.Controllers;

[ApiController]
[Authorize]
[RegistrationProblemDetails]
[Route("api/study-sessions/active")]
public sealed class ActiveStudySessionController(ISessionService sessions, ILogger<ActiveStudySessionController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ActiveStudySessionResponse>(200)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(500)]
    public async Task<IActionResult> Get([FromServices] GetActiveStudySession get, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var result = await get.ExecuteAsync(ct);
            if (result.Status == StudySessionReadStatus.AuthenticationRequired)
            {
                await sessions.LogoutAsync(ct);
                return Error(401, "authentication_required", "Autenticação necessária.");
            }
            if (result.Status != StudySessionReadStatus.Success) throw new InvalidOperationException("Unexpected active lookup result.");
            var active = result.Active;
            return Ok(new ActiveStudySessionResponse(active is null ? null : new(
                StudySessionResponseMapping.ToResponse(active.Session), active.MapId, active.NodeId, active.NodeTitle), result.ServerTimeUtc));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError("Active study session lookup failed due to an unexpected operational error.");
            return Error(500, "unexpected_error", "Não foi possível concluir a operação.");
        }
    }

    private static ObjectResult Error(int status, string code, string title) => new(
        new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } }) { StatusCode = status };
}
