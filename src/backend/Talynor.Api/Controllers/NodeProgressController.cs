using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Talynor.Api.Contracts;
using Talynor.Api.Filters;
using Talynor.Application.Accounts;
using Talynor.Application.Progress;

namespace Talynor.Api.Controllers;

[ApiController]
[Authorize]
[RegistrationProblemDetails]
[Route("api/maps/{mapId:guid}/nodes/{nodeId:guid}/progress")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(500)]
public sealed class NodeProgressController(ISessionService sessions, ILogger<NodeProgressController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<NodeProgressResponse>(200)]
    public async Task<IActionResult> Get(Guid mapId, Guid nodeId, [FromServices] GetNodeProgress get,
        CancellationToken ct, [FromQuery] int days = 30)
    {
        Response.Headers.CacheControl = "no-store";
        if (Request.Query.Any(pair => pair.Key != "days" || pair.Value.Count != 1))
            return Error(400, "validation_failed", "Confira os campos informados.");

        try
        {
            var result = await get.ExecuteAsync(mapId, nodeId, days, ct);
            if (result.Status == NodeProgressStatus.Success)
                return Ok(NodeProgressResponseMapping.ToResponse(result.Summary!));
            if (result.Status == NodeProgressStatus.AuthenticationRequired) await sessions.LogoutAsync(ct);
            return result.Status switch
            {
                NodeProgressStatus.AuthenticationRequired => Error(401, "authentication_required", "Autenticação necessária."),
                NodeProgressStatus.NotFound => Error(404, "progress_not_found", "Recurso não encontrado."),
                NodeProgressStatus.Invalid => Error(400, "validation_failed", "Confira os campos informados."),
                _ => throw new InvalidOperationException("Unexpected node progress result.")
            };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError("Node progress read failed due to an unexpected operational error.");
            return Error(500, "unexpected_error", "Não foi possível concluir a operação.");
        }
    }

    private static ObjectResult Error(int status, string code, string title) => new(
        new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } }) { StatusCode = status };
}
