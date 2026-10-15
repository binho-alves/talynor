using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Talynor.Api.Contracts;
using Talynor.Api.Filters;
using Talynor.Application.Accounts;
using Talynor.Application.KnowledgeMaps;

namespace Talynor.Api.Controllers;

[ApiController]
[Route("api/maps")]
[Authorize]
[AutoValidateAntiforgeryToken]
[RegistrationProblemDetails] // Existing endpoint-only input/CSRF sanitizer; auth behavior is unchanged.
[RequestSizeLimit(32 * 1024)] // Fits bounded text even when JSON Unicode is escaped.
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(500)]
public sealed class KnowledgeAuthoringController(ISessionService sessions, ILogger<KnowledgeAuthoringController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<KnowledgeMapAuthoringResponse>(201)]
    public Task<IActionResult> CreateMap(CreateMapRequest request, [FromServices] CreateKnowledgeMap create, CancellationToken ct) =>
        Execute(async () =>
        {
            var result = await create.ExecuteAsync(new(request.Title, request.Description), ct);
            var map = result.Value;
            return (result.Status, map is null ? null : new KnowledgeMapAuthoringResponse(map.Id, map.Title, map.Description, map.CreatedAtUtc, map.UpdatedAtUtc));
        }, "map", true, ct);

    [HttpPatch("{mapId:guid}")]
    [ProducesResponseType(204)]
    public Task<IActionResult> UpdateMap(Guid mapId, UpdateMapRequest request, [FromServices] UpdateKnowledgeMap update, CancellationToken ct) =>
        Execute(async () => (await update.ExecuteAsync(mapId, new(request.Title, request.Description, request.ClearDescription), ct), null), "map", false, ct);

    [HttpDelete("{mapId:guid}")]
    [ProducesResponseType(204)]
    public Task<IActionResult> DeleteMap(Guid mapId, [FromServices] DeleteKnowledgeMap delete, CancellationToken ct) =>
        Execute(async () => (await delete.ExecuteAsync(mapId, ct), null), "map", false, ct);

    [HttpPost("{mapId:guid}/nodes")]
    [ProducesResponseType<KnowledgeNodeAuthoringResponse>(201)]
    public Task<IActionResult> CreateNode(Guid mapId, CreateNodeRequest request, [FromServices] CreateKnowledgeNode create, CancellationToken ct) =>
        Execute(async () =>
        {
            var result = await create.ExecuteAsync(mapId, new(request.Title, request.Description, request.Position.X, request.Position.Y, request.ChildMapId), ct);
            var node = result.Value;
            return (result.Status, node is null ? null : new KnowledgeNodeAuthoringResponse(node.Id, node.Title, node.Description,
                new(node.PositionX, node.PositionY), node.ChildMapId, node.CreatedAtUtc, node.UpdatedAtUtc));
        }, "node", true, ct);

    [HttpPatch("{mapId:guid}/nodes/{nodeId:guid}")]
    [ProducesResponseType(204)]
    public Task<IActionResult> UpdateNode(Guid mapId, Guid nodeId, UpdateNodeRequest request, [FromServices] UpdateKnowledgeNode update, CancellationToken ct) =>
        Execute(async () => (await update.ExecuteAsync(mapId, nodeId, new(request.Title, request.Description, request.ClearDescription,
            request.Position is null ? null : new(request.Position.X, request.Position.Y), request.ChildMapId, request.ClearChildMap), ct), null), "node", false, ct);

    [HttpDelete("{mapId:guid}/nodes/{nodeId:guid}")]
    [ProducesResponseType(204)]
    public Task<IActionResult> DeleteNode(Guid mapId, Guid nodeId, [FromServices] DeleteKnowledgeNode delete, CancellationToken ct) =>
        Execute(async () => (await delete.ExecuteAsync(mapId, nodeId, ct), null), "node", false, ct);

    [HttpPost("{mapId:guid}/edges")]
    [ProducesResponseType<KnowledgeEdgeAuthoringResponse>(201)]
    public Task<IActionResult> CreateEdge(Guid mapId, CreateEdgeRequest request, [FromServices] CreateKnowledgeEdge create, CancellationToken ct) =>
        Execute(async () =>
        {
            var result = await create.ExecuteAsync(mapId, new(request.SourceNodeId, request.TargetNodeId), ct);
            var edge = result.Value;
            return (result.Status, edge is null ? null : new KnowledgeEdgeAuthoringResponse(edge.Id, edge.SourceNodeId, edge.TargetNodeId, edge.CreatedAtUtc));
        }, "edge", true, ct);

    [HttpDelete("{mapId:guid}/edges/{edgeId:guid}")]
    [ProducesResponseType(204)]
    public Task<IActionResult> DeleteEdge(Guid mapId, Guid edgeId, [FromServices] DeleteKnowledgeEdge delete, CancellationToken ct) =>
        Execute(async () => (await delete.ExecuteAsync(mapId, edgeId, ct), null), "edge", false, ct);

    private async Task<IActionResult> Execute(Func<Task<(KnowledgeWriteStatus Status, object? Body)>> action,
        string resource, bool created, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var (status, body) = await action();
            if (status == KnowledgeWriteStatus.Success)
            {
                if (!created) return NoContent();
                if (body is KnowledgeMapAuthoringResponse map) Response.Headers.Location = $"/api/maps/{map.Id:D}";
                return StatusCode(201, body);
            }
            if (status == KnowledgeWriteStatus.AuthenticationRequired) await sessions.LogoutAsync(ct);
            return status switch
            {
                KnowledgeWriteStatus.AuthenticationRequired => Error(401, "authentication_required", "Autenticação necessária."),
                KnowledgeWriteStatus.NotFound => Error(404, resource + "_not_found", "Recurso não encontrado."),
                KnowledgeWriteStatus.InvalidInput => Error(400, "validation_failed", "Confira os campos informados."),
                KnowledgeWriteStatus.InvalidChildMap => Error(400, "invalid_child_map", "Submapa inválido."),
                KnowledgeWriteStatus.InvalidEdgeNodes => Error(400, "invalid_edge_nodes", "Nós da conexão inválidos."),
                KnowledgeWriteStatus.MapInUse => Error(409, "map_in_use", "O mapa possui referências e não pode ser removido."),
                KnowledgeWriteStatus.NodeInUse => Error(409, "node_in_use", "O nó possui conexões e não pode ser removido."),
                _ => throw new InvalidOperationException("Unexpected authoring result.")
            };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError("Knowledge graph authoring failed due to an unexpected operational error.");
            return Error(500, "unexpected_error", "Não foi possível concluir a operação.");
        }
    }

    private static ObjectResult Error(int status, string code, string title) => new(
        new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } }) { StatusCode = status };
}
