using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Talynor.Api.Contracts;
using Talynor.Application.Accounts;
using Talynor.Application.KnowledgeMaps;

namespace Talynor.Api.Controllers;

[ApiController]
[Route("api/maps")]
[Authorize]
public sealed class PersistedKnowledgeMapsController : ControllerBase
{
    [HttpGet("{mapId:guid}")]
    [ProducesResponseType<PersistedKnowledgeMapResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get(Guid mapId,
        [FromServices] GetKnowledgeMapForCurrentWorkspace getMap, [FromServices] ISessionService sessions,
        [FromServices] ILogger<PersistedKnowledgeMapsController> logger, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var result = await getMap.ExecuteAsync(mapId, cancellationToken);
            if (result.Status == KnowledgeMapReadStatus.AuthenticationRequired)
            {
                await sessions.LogoutAsync(cancellationToken);
                return MapProblem(401, "authentication_required", "Autenticação necessária.");
            }
            if (result.Status == KnowledgeMapReadStatus.NotFound)
                return MapProblem(404, "map_not_found", "Mapa não encontrado.");
            var map = result.Map!;
            return Ok(new PersistedKnowledgeMapResponse(map.Id, map.Title, map.Description,
                map.Nodes.Select(node => new PersistedKnowledgeNodeResponse(node.Id, node.Title,
                    node.Description, new(node.PositionX, node.PositionY), node.ChildMapId)).ToArray(),
                map.Edges.Select(edge => new PersistedKnowledgeEdgeResponse(
                    edge.Id, edge.SourceNodeId, edge.TargetNodeId)).ToArray()));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Persisted knowledge map read failed due to an unexpected operational error.");
            return MapProblem(500, "unexpected_error", "Não foi possível concluir a operação.");
        }
    }

    private static ObjectResult MapProblem(int status, string code, string title) => new(
        new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } })
    { StatusCode = status };
}
