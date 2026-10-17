using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Talynor.Api.Contracts;
using Talynor.Api.Filters;
using Talynor.Application.Accounts;
using Talynor.Application.KnowledgeContents;

namespace Talynor.Api.Controllers;

[ApiController]
[Authorize]
[AutoValidateAntiforgeryToken]
[RegistrationProblemDetails]
[RequestSizeLimit(32 * 1024)] // Same MVC policy as graph authoring, including DELETE.
[Route("api/maps/{mapId:guid}/nodes/{nodeId:guid}/contents")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(500)]
public sealed class KnowledgeContentAuthoringController(ISessionService sessions,
    ILogger<KnowledgeContentAuthoringController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<KnowledgeContentDetailResponse>(201)]
    public Task<IActionResult> Create(Guid mapId, Guid nodeId, CreateKnowledgeContentRequest request,
        [FromServices] CreateKnowledgeContent create, CancellationToken ct) => Execute(async () =>
    {
        var result = await create.ExecuteAsync(mapId, nodeId, new(request.Type, request.Title, request.Body, request.Url,
            request.BodySpecified, request.UrlSpecified), ct);
        return (result.Status, result.Content);
    }, mapId, nodeId, ct);

    [HttpPatch("{contentId:guid}")]
    [ProducesResponseType(204)]
    public Task<IActionResult> Update(Guid mapId, Guid nodeId, Guid contentId, UpdateKnowledgeContentRequest request,
        [FromServices] UpdateKnowledgeContent update, CancellationToken ct) => Execute(async () =>
        (await update.ExecuteAsync(mapId, nodeId, contentId, new(request.Title, request.Body, request.Url, request.ClearTitle,
            request.BodySpecified, request.UrlSpecified), ct), null), mapId, nodeId, ct);

    [HttpDelete("{contentId:guid}")]
    [ProducesResponseType(204)]
    public Task<IActionResult> Delete(Guid mapId, Guid nodeId, Guid contentId,
        [FromServices] DeleteKnowledgeContent delete, CancellationToken ct) => Execute(async () =>
        (await delete.ExecuteAsync(mapId, nodeId, contentId, ct), null), mapId, nodeId, ct);

    private async Task<IActionResult> Execute(Func<Task<(KnowledgeContentWriteStatus Status, KnowledgeContentDetail? Content)>> action,
        Guid mapId, Guid nodeId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var (status, content) = await action();
            if (status == KnowledgeContentWriteStatus.Success)
            {
                if (content is null) return NoContent();
                Response.Headers.Location = $"/api/maps/{mapId:D}/nodes/{nodeId:D}/contents/{content.Id:D}";
                return StatusCode(201, KnowledgeContentResponseMapping.ToResponse(content));
            }
            if (status == KnowledgeContentWriteStatus.AuthenticationRequired) await sessions.LogoutAsync(ct);
            return status switch
            {
                KnowledgeContentWriteStatus.AuthenticationRequired => Error(401, "authentication_required", "Autenticação necessária."),
                KnowledgeContentWriteStatus.NotFound => Error(404, "content_not_found", "Recurso não encontrado."),
                KnowledgeContentWriteStatus.InvalidInput => Error(400, "validation_failed", "Confira os campos informados."),
                _ => throw new InvalidOperationException("Unexpected content authoring result.")
            };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError("Knowledge content authoring failed due to an unexpected operational error.");
            return Error(500, "unexpected_error", "Não foi possível concluir a operação.");
        }
    }

    private static ObjectResult Error(int status, string code, string title) => new(
        new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } }) { StatusCode = status };
}
