using Microsoft.AspNetCore.Mvc;
using Talynor.Api.Contracts;
using Talynor.Application.KnowledgeMaps;

namespace Talynor.Api.Controllers;

[ApiController]
[Route("api/knowledge-maps")]
public sealed class KnowledgeMapsController(GetKnowledgeMap getKnowledgeMap) : ControllerBase
{
    [HttpGet("{mapId}")]
    [ProducesResponseType<KnowledgeMapResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KnowledgeMapResponse>> Get(string mapId, CancellationToken cancellationToken)
    {
        var map = await getKnowledgeMap.ExecuteAsync(mapId, cancellationToken);
        if (map is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Mapa não encontrado");
        }

        return Ok(new KnowledgeMapResponse(map.Id, map.Title, map.Description,
            map.Nodes.Select(node => new KnowledgeNodeResponse(node.Id, node.Title,
                node.Description, node.Progress, node.StudyMinutes, node.ChildMapId)).ToArray(),
            map.Edges.Select(edge => new KnowledgeEdgeResponse(
                edge.Id, edge.SourceNodeId, edge.TargetNodeId)).ToArray()));
    }
}
