using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Talynor.Api.Contracts;
using Talynor.Application.Accounts;
using Talynor.Application.Workspaces;

namespace Talynor.Api.Controllers;

[ApiController]
[Route("api/workspace")]
[Authorize]
public sealed class WorkspaceController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PersonalWorkspaceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get([FromServices] GetOrCreatePersonalWorkspace getWorkspace,
        [FromServices] ISessionService sessions, [FromServices] ILogger<WorkspaceController> logger,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var workspace = await getWorkspace.ExecuteAsync(cancellationToken);
            if (workspace is not null) return Ok(new PersonalWorkspaceResponse(workspace.Id, workspace.CreatedAtUtc));
            await sessions.LogoutAsync(cancellationToken);
            return WorkspaceProblem(StatusCodes.Status401Unauthorized, "authentication_required", "Autenticação necessária.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Personal workspace operation failed due to an unexpected operational error.");
            return WorkspaceProblem(StatusCodes.Status500InternalServerError, "unexpected_error",
                "Não foi possível concluir a operação.");
        }
    }

    private static ObjectResult WorkspaceProblem(int status, string code, string title) => new(
        new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } })
    { StatusCode = status };
}
