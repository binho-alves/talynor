using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Talynor.Api.Contracts;
using Talynor.Api.Filters;
using Talynor.Application.Accounts;
using Talynor.Application.Quizzes;

namespace Talynor.Api.Controllers;

[ApiController]
[Authorize]
[AutoValidateAntiforgeryToken]
[RegistrationProblemDetails]
[Route("api/maps/{mapId:guid}/nodes/{nodeId:guid}/quizzes")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(500)]
public sealed class QuizzesController(ISessionService sessions, ILogger<QuizzesController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<QuizCollectionResponse>(200)]
    public Task<IActionResult> List(Guid mapId, Guid nodeId, [FromServices] ListQuizzes list, CancellationToken ct,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (Request.Query.Any(pair => pair.Key is not ("page" or "pageSize") || pair.Value.Count != 1))
        {
            Response.Headers.CacheControl = "no-store";
            return Task.FromResult<IActionResult>(Error(400, "validation_failed", "Confira os campos informados."));
        }
        return Execute(async () =>
        {
            var result = await list.ExecuteAsync(mapId, nodeId, page, pageSize, ct);
            var value = result.Collection;
            return (result.Status, value is null ? null : new QuizCollectionResponse(value.Quizzes.Select(q =>
                new QuizSummaryResponse(q.Id, q.Title, q.ContentRevision, q.CreatedAtUtc, q.UpdatedAtUtc)).ToArray(),
                value.Page, value.PageSize, value.HasMore));
        }, ct);
    }

    [HttpGet("{quizId:guid}")]
    [ProducesResponseType<QuizDefinitionResponse>(200)]
    public Task<IActionResult> Get(Guid mapId, Guid nodeId, Guid quizId, [FromServices] GetQuiz get, CancellationToken ct) =>
        Execute(async () =>
        {
            var result = await get.ExecuteAsync(mapId, nodeId, quizId, ct);
            return (result.Status, result.Definition is null ? null : QuizResponseMapping.ToResponse(result.Definition));
        }, ct);

    [HttpGet("{quizId:guid}/take")]
    [ProducesResponseType<QuizTakeResponse>(200)]
    public Task<IActionResult> Take(Guid mapId, Guid nodeId, Guid quizId, [FromServices] TakeQuiz take, CancellationToken ct) =>
        Execute(async () =>
        {
            var result = await take.ExecuteAsync(mapId, nodeId, quizId, ct);
            return (result.Status, result.Definition is null ? null : QuizResponseMapping.ToResponse(result.Definition));
        }, ct);

    [HttpPost]
    [RequestSizeLimit(QuizBodyLimitAttribute.Limit)]
    [QuizBodyLimit]
    [ProducesResponseType<QuizDefinitionResponse>(201)]
    [ProducesResponseType<ProblemDetails>(413)]
    public Task<IActionResult> Create(Guid mapId, Guid nodeId, CreateQuizRequest request, [FromServices] CreateQuiz create, CancellationToken ct) =>
        Execute(async () =>
        {
            var result = await create.ExecuteAsync(mapId, nodeId, request.ToInput(), ct);
            return (result.Status, result.Definition is null ? null : QuizResponseMapping.ToResponse(result.Definition));
        }, ct, created: true, mapId, nodeId);

    [HttpPut("{quizId:guid}")]
    [RequestSizeLimit(QuizBodyLimitAttribute.Limit)]
    [QuizBodyLimit]
    [ProducesResponseType(204)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(413)]
    public Task<IActionResult> Replace(Guid mapId, Guid nodeId, Guid quizId, ReplaceQuizRequest request,
        [FromServices] ReplaceQuiz replace, CancellationToken ct) => Execute(async () =>
        (await replace.ExecuteAsync(mapId, nodeId, quizId, request.ExpectedRevision ?? 0, request.ToInput(), ct), null), ct);

    [HttpDelete("{quizId:guid}")]
    [QuizNoBody]
    [ProducesResponseType(204)]
    public Task<IActionResult> Delete(Guid mapId, Guid nodeId, Guid quizId, [FromServices] DeleteQuiz delete, CancellationToken ct) =>
        Execute(async () => (await delete.ExecuteAsync(mapId, nodeId, quizId, ct), null), ct);

    private async Task<IActionResult> Execute(Func<Task<(QuizStatus Status, object? Body)>> action, CancellationToken ct,
        bool created = false, Guid mapId = default, Guid nodeId = default)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var (status, body) = await action();
            if (status == QuizStatus.Success)
            {
                if (created && body is QuizDefinitionResponse quiz)
                {
                    Response.Headers.Location = $"/api/maps/{mapId:D}/nodes/{nodeId:D}/quizzes/{quiz.Id:D}";
                    return StatusCode(201, quiz);
                }
                return body is null ? NoContent() : Ok(body);
            }
            if (status == QuizStatus.AuthenticationRequired) await sessions.LogoutAsync(ct);
            return status switch
            {
                QuizStatus.AuthenticationRequired => Error(401, "authentication_required", "Autenticação necessária."),
                QuizStatus.NotFound => Error(404, "quiz_not_found", "Recurso não encontrado."),
                QuizStatus.InvalidInput => Error(400, "validation_failed", "Confira os campos informados."),
                QuizStatus.ContentChanged => Error(409, "quiz_changed", "O quiz foi alterado. Busque-o novamente."),
                _ => throw new InvalidOperationException("Unexpected quiz result.")
            };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError("Quiz operation failed due to an unexpected operational error.");
            return Error(500, "unexpected_error", "Não foi possível concluir a operação.");
        }
    }

    private static ObjectResult Error(int status, string code, string title) => new(
        new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } }) { StatusCode = status };
}
