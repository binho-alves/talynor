using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Talynor.Api.Contracts;
using Talynor.Application.Accounts;
using Talynor.Application.KnowledgeContents;

namespace Talynor.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/maps/{mapId:guid}/nodes/{nodeId:guid}/contents")]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(500)]
public sealed class KnowledgeContentsController(ISessionService sessions, ILogger<KnowledgeContentsController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<KnowledgeContentListResponse>(200)]
    public Task<IActionResult> List(Guid mapId, Guid nodeId, [FromServices] ListKnowledgeContentsForNode list, CancellationToken ct) =>
        Execute(async () =>
        {
            var result = await list.ExecuteAsync(mapId, nodeId, ct);
            return (result.Status, result.Contents is null ? null : new KnowledgeContentListResponse(result.Contents.Select(content =>
                new KnowledgeContentSummaryResponse(content.Id, KnowledgeContentResponseMapping.TypeName(content.Type), content.Title,
                    content.CreatedAtUtc, content.UpdatedAtUtc, content.Url)).ToArray()));
        }, ct);

    [HttpGet("{contentId:guid}")]
    [ProducesResponseType<KnowledgeContentDetailResponse>(200)]
    public Task<IActionResult> Get(Guid mapId, Guid nodeId, Guid contentId, [FromServices] GetKnowledgeContentForNode get, CancellationToken ct) =>
        Execute(async () =>
        {
            var result = await get.ExecuteAsync(mapId, nodeId, contentId, ct);
            var content = result.Content;
            return (result.Status, content is null ? null : KnowledgeContentResponseMapping.ToResponse(content));
        }, ct);

    private async Task<IActionResult> Execute(Func<Task<(KnowledgeContentReadStatus Status, object? Body)>> action, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var (status, body) = await action();
            if (status == KnowledgeContentReadStatus.Success) return Ok(body);
            if (status == KnowledgeContentReadStatus.AuthenticationRequired)
            {
                await sessions.LogoutAsync(ct);
                return Error(401, "authentication_required", "Autenticação necessária.");
            }
            if (status == KnowledgeContentReadStatus.NotFound)
                return Error(404, "content_not_found", "Recurso não encontrado.");
            throw new InvalidOperationException("Unexpected content read result.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError("Knowledge content read failed due to an unexpected operational error.");
            return Error(500, "unexpected_error", "Não foi possível concluir a operação.");
        }
    }

    private static ObjectResult Error(int status, string code, string title) => new(
        new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } }) { StatusCode = status };
}
