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
[Route("api/maps/{mapId:guid}/nodes/{nodeId:guid}/study-sessions")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(500)]
public sealed class StudySessionsController(ISessionService sessions, ILogger<StudySessionsController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<StudySessionHistoryResponse>(200)]
    public Task<IActionResult> List(Guid mapId, Guid nodeId, [FromServices] ListStudySessionsForNode list,
        CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Execute(async () =>
        {
            var result = await list.ExecuteAsync(mapId, nodeId, page, pageSize, ct);
            var history = result.History;
            return (result.Status, history is null ? null : new StudySessionHistoryResponse(
                history.Sessions.Select(StudySessionResponseMapping.ToResponse).ToArray(), history.Page, history.PageSize, history.HasMore));
        }, ct);

    [HttpGet("{sessionId:guid}")]
    [ProducesResponseType<StudySessionResponse>(200)]
    public Task<IActionResult> Get(Guid mapId, Guid nodeId, Guid sessionId, [FromServices] GetStudySessionForNode get, CancellationToken ct) =>
        Execute(async () =>
        {
            var result = await get.ExecuteAsync(mapId, nodeId, sessionId, ct);
            return (result.Status, result.Session is null ? null : StudySessionResponseMapping.ToResponse(result.Session));
        }, ct);

    private async Task<IActionResult> Execute(Func<Task<(StudySessionReadStatus Status, object? Body)>> action, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var (status, body) = await action();
            if (status == StudySessionReadStatus.Success) return Ok(body);
            if (status == StudySessionReadStatus.AuthenticationRequired)
            {
                await sessions.LogoutAsync(ct);
                return Error(401, "authentication_required", "Autenticação necessária.");
            }
            if (status == StudySessionReadStatus.NotFound) return Error(404, "study_session_not_found", "Recurso não encontrado.");
            if (status == StudySessionReadStatus.Invalid) return Error(400, "validation_failed", "Paginação inválida.");
            throw new InvalidOperationException("Unexpected study session read result.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError("Study session read failed due to an unexpected operational error.");
            return Error(500, "unexpected_error", "Não foi possível concluir a operação.");
        }
    }

    private static ObjectResult Error(int status, string code, string title) => new(
        new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } }) { StatusCode = status };
}
