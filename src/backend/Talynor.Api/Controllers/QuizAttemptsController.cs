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
[Route("api/maps/{mapId:guid}/nodes/{nodeId:guid}/quizzes/{quizId:guid}/attempts")]
public sealed class QuizAttemptsController(ISessionService sessions, ILogger<QuizAttemptsController> logger) : ControllerBase
{
    public const int SubmissionLimit = 16 * 1024;

    [HttpPut("{attemptId:guid}")]
    [RequestSizeLimit(SubmissionLimit)]
    [QuizBodyLimit(SubmissionLimit)]
    [ProducesResponseType<QuizAttemptResponse>(200)]
    public Task<IActionResult> Submit(Guid mapId, Guid nodeId, Guid quizId, Guid attemptId,
        SubmitQuizAttemptRequest request, [FromServices] SubmitQuizAttempt submit, CancellationToken ct) =>
        Execute(async () =>
        {
            var result = await submit.ExecuteAsync(mapId, nodeId, quizId, attemptId, request.ContentRevision ?? 0, request.ToAnswers(), ct);
            return (result.Status, result.Attempt is null ? null : QuizAttemptResponseMapping.ToResponse(result.Attempt));
        }, ct);

    [HttpGet]
    [ProducesResponseType<QuizAttemptCollectionResponse>(200)]
    public Task<IActionResult> List(Guid mapId, Guid nodeId, Guid quizId, [FromServices] ListQuizAttempts list,
        CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (Request.Query.Any(pair => pair.Key is not ("page" or "pageSize") || pair.Value.Count != 1))
        {
            Response.Headers.CacheControl = "no-store";
            return Task.FromResult<IActionResult>(Error(400, "validation_failed", "Confira os campos informados."));
        }
        return Execute(async () =>
        {
            var result = await list.ExecuteAsync(mapId, nodeId, quizId, page, pageSize, ct);
            var value = result.Collection;
            return (result.Status, value is null ? null : new QuizAttemptCollectionResponse(
                value.Attempts.Select(QuizAttemptResponseMapping.ToResponse).ToArray(), value.Page, value.PageSize, value.HasMore));
        }, ct);
    }

    [HttpGet("{attemptId:guid}")]
    [ProducesResponseType<QuizAttemptResponse>(200)]
    public Task<IActionResult> Get(Guid mapId, Guid nodeId, Guid quizId, Guid attemptId, [FromServices] GetQuizAttempt get, CancellationToken ct) =>
        Execute(async () =>
        {
            var result = await get.ExecuteAsync(mapId, nodeId, quizId, attemptId, ct);
            return (result.Status, result.Attempt is null ? null : QuizAttemptResponseMapping.ToResponse(result.Attempt));
        }, ct);

    private async Task<IActionResult> Execute(Func<Task<(QuizStatus Status, object? Body)>> action, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var (status, body) = await action();
            if (status == QuizStatus.Success) return Ok(body);
            if (status == QuizStatus.AuthenticationRequired) await sessions.LogoutAsync(ct);
            return status switch
            {
                QuizStatus.AuthenticationRequired => Error(401, "authentication_required", "Autenticação necessária."),
                QuizStatus.NotFound => Error(404, "quiz_not_found", "Recurso não encontrado."),
                QuizStatus.InvalidInput => Error(400, "validation_failed", "Confira os campos informados."),
                QuizStatus.ContentChanged => Error(409, "quiz_changed", "O quiz foi alterado. Busque-o novamente."),
                QuizStatus.AttemptIdConflict => Error(409, "attempt_id_conflict", "O identificador da tentativa já foi utilizado."),
                _ => throw new InvalidOperationException("Unexpected quiz attempt result.")
            };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError("Quiz attempt operation failed due to an unexpected operational error.");
            return Error(500, "unexpected_error", "Não foi possível concluir a operação.");
        }
    }

    private static ObjectResult Error(int status, string code, string title) => new(
        new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } }) { StatusCode = status };
}
