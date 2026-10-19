using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Talynor.Api.Contracts;
using Talynor.Api.Filters;
using Talynor.Application.Accounts;
using Talynor.Application.StudySessions;

namespace Talynor.Api.Controllers;

[ApiController]
[Authorize]
[AutoValidateAntiforgeryToken]
[RegistrationProblemDetails]
[StudySessionNoBody]
[Route("api/maps/{mapId:guid}/nodes/{nodeId:guid}/study-sessions")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(500)]
public sealed class StudySessionAuthoringController(ISessionService sessions,
    ILogger<StudySessionAuthoringController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<StudySessionResponse>(201)]
    public Task<IActionResult> Start(Guid mapId, Guid nodeId, [FromServices] StartStudySession start, CancellationToken ct) =>
        Execute(() => start.ExecuteAsync(mapId, nodeId, ct), mapId, nodeId, ct);

    [HttpPost("{sessionId:guid}/complete")]
    [ProducesResponseType(204)]
    public Task<IActionResult> Complete(Guid mapId, Guid nodeId, Guid sessionId, [FromServices] CompleteStudySession complete,
        CancellationToken ct) => Execute(async () => new(await complete.ExecuteAsync(mapId, nodeId, sessionId, ct)), mapId, nodeId, ct);

    [HttpDelete("{sessionId:guid}")]
    [ProducesResponseType(204)]
    public Task<IActionResult> Delete(Guid mapId, Guid nodeId, Guid sessionId, [FromServices] DeleteStudySession delete,
        CancellationToken ct) => Execute(async () => new(await delete.ExecuteAsync(mapId, nodeId, sessionId, ct)), mapId, nodeId, ct);

    private async Task<IActionResult> Execute(Func<Task<StartStudySessionResult>> action, Guid mapId, Guid nodeId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var result = await action();
            if (result.Status == StudySessionWriteStatus.Success)
            {
                if (result.Session is null) return NoContent();
                Response.Headers.Location = $"/api/maps/{mapId:D}/nodes/{nodeId:D}/study-sessions/{result.Session.Id:D}";
                return StatusCode(201, StudySessionResponseMapping.ToResponse(result.Session));
            }
            if (result.Status == StudySessionWriteStatus.AuthenticationRequired) await sessions.LogoutAsync(ct);
            return result.Status switch
            {
                StudySessionWriteStatus.AuthenticationRequired => Error(401, "authentication_required", "Autenticação necessária."),
                StudySessionWriteStatus.NotFound => Error(404, "study_session_not_found", "Recurso não encontrado."),
                StudySessionWriteStatus.ActiveSessionExists => Error(409, "active_session_exists", "Já existe uma sessão de estudo ativa."),
                StudySessionWriteStatus.ClockConflict => Error(409, "study_session_clock_conflict", "O horário de término é anterior ao início."),
                _ => throw new InvalidOperationException("Unexpected study session write result.")
            };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError("Study session write failed due to an unexpected operational error.");
            return Error(500, "unexpected_error", "Não foi possível concluir a operação.");
        }
    }

    private static ObjectResult Error(int status, string code, string title) => new(
        new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } }) { StatusCode = status };
}
