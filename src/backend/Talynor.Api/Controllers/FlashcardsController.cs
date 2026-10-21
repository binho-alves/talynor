using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using Talynor.Api.Contracts;
using Talynor.Api.Filters;
using Talynor.Application.Accounts;
using Talynor.Application.Flashcards;

namespace Talynor.Api.Controllers;

[ApiController]
[Authorize]
[AutoValidateAntiforgeryToken]
[RegistrationProblemDetails]
[Route("api/maps/{mapId:guid}/nodes/{nodeId:guid}/flashcards")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(500)]
public sealed class FlashcardsController(ISessionService sessions, ILogger<FlashcardsController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<FlashcardCollectionResponse>(200)]
    public Task<IActionResult> List(Guid mapId, Guid nodeId, [FromServices] ListFlashcards list, CancellationToken ct,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20) => Execute(async () =>
    {
        var result = await list.ExecuteAsync(mapId, nodeId, page, pageSize, ct);
        var pageResult = result.Collection;
        return (result.Status, pageResult is null ? null : new FlashcardCollectionResponse(
            pageResult.Flashcards.Select(FlashcardResponseMapping.ToResponse).ToArray(), pageResult.Page, pageResult.PageSize, pageResult.HasMore));
    }, ct);

    [HttpGet("{flashcardId:guid}")]
    [ProducesResponseType<FlashcardResponse>(200)]
    public Task<IActionResult> Get(Guid mapId, Guid nodeId, Guid flashcardId, [FromServices] GetFlashcard get, CancellationToken ct) =>
        Execute(async () =>
        {
            var result = await get.ExecuteAsync(mapId, nodeId, flashcardId, ct);
            return (result.Status, result.Card is null ? null : FlashcardResponseMapping.ToResponse(result.Card));
        }, ct);

    [HttpPost]
    [RequestSizeLimit(FlashcardBodyLimitAttribute.Limit)]
    [FlashcardBodyLimit]
    [ProducesResponseType<FlashcardResponse>(201)]
    [ProducesResponseType<ProblemDetails>(413)]
    public Task<IActionResult> Create(Guid mapId, Guid nodeId, CreateFlashcardRequest request,
        [FromServices] CreateFlashcard create, CancellationToken ct) => Execute(async () =>
    {
        var result = await create.ExecuteAsync(mapId, nodeId, new(request.Front, request.Back), ct);
        return (result.Status, result.Card is null ? null : FlashcardResponseMapping.ToResponse(result.Card));
    }, ct, created: true, mapId, nodeId);

    [HttpPatch("{flashcardId:guid}")]
    [RequestSizeLimit(FlashcardBodyLimitAttribute.Limit)]
    [FlashcardBodyLimit]
    [ProducesResponseType(204)]
    [ProducesResponseType<ProblemDetails>(413)]
    public Task<IActionResult> Update(Guid mapId, Guid nodeId, Guid flashcardId, UpdateFlashcardRequest request,
        [FromServices] UpdateFlashcard update, CancellationToken ct) => Execute(async () =>
        (await update.ExecuteAsync(mapId, nodeId, flashcardId,
            new(request.Front, request.Back, request.FrontSpecified, request.BackSpecified), ct), null), ct);

    [HttpDelete("{flashcardId:guid}")]
    [ProducesResponseType(204)]
    public Task<IActionResult> Delete(Guid mapId, Guid nodeId, Guid flashcardId,
        [FromServices] DeleteFlashcard delete, CancellationToken ct) => Execute(async () =>
        (await delete.ExecuteAsync(mapId, nodeId, flashcardId, ct), null), ct);

    [HttpGet("study/next")]
    [ProducesResponseType<FlashcardStudyNextResponse>(200)]
    public Task<IActionResult> StudyNext(Guid mapId, Guid nodeId,
        [FromServices] GetNextFlashcardForStudy getNext, CancellationToken ct) => Execute(async () =>
    {
        var result = await getNext.ExecuteAsync(mapId, nodeId, ct);
        return (result.Status, result.Status == FlashcardStatus.Success
            ? FlashcardResponseMapping.ToStudyNextResponse(result.Item) : null);
    }, ct);

    [HttpGet("{flashcardId:guid}/study/reveal")]
    [ProducesResponseType<FlashcardRevealResponse>(200)]
    [ProducesResponseType<ProblemDetails>(409)]
    public Task<IActionResult> Reveal(Guid mapId, Guid nodeId, Guid flashcardId,
        [FromServices] RevealFlashcard reveal, CancellationToken ct)
    {
        if (Request.Query.Count != 1 || !Request.Query.TryGetValue("contentRevision", out var values) ||
            values.Count != 1 || !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
        {
            Response.Headers.CacheControl = "no-store";
            return Task.FromResult<IActionResult>(Error(400, "validation_failed", "Confira os campos informados."));
        }
        return Execute(async () =>
        {
            var result = await reveal.ExecuteAsync(mapId, nodeId, flashcardId, revision, ct);
            return (result.Status, result.Reveal is null ? null : new FlashcardRevealResponse(
                result.Reveal.FlashcardId, result.Reveal.Back, result.Reveal.ContentRevision));
        }, ct);
    }

    [HttpPut("{flashcardId:guid}/reviews/{reviewId:guid}")]
    [RequestSizeLimit(FlashcardBodyLimitAttribute.Limit)]
    [FlashcardBodyLimit]
    [ProducesResponseType<FlashcardReviewEnvelopeResponse>(200)]
    [ProducesResponseType<ProblemDetails>(409)]
    public Task<IActionResult> Review(Guid mapId, Guid nodeId, Guid flashcardId, Guid reviewId,
        ReviewFlashcardRequest request, [FromServices] ReviewFlashcard review, CancellationToken ct) => Execute(async () =>
    {
        var result = await review.ExecuteAsync(mapId, nodeId, flashcardId, reviewId,
            request.ContentRevision ?? 0, request.Result, ct);
        return (result.Status, result.View is null ? null : FlashcardResponseMapping.ToReviewResponse(result.View));
    }, ct);

    [HttpGet("{flashcardId:guid}/reviews/{reviewId:guid}")]
    [ProducesResponseType<FlashcardReviewEnvelopeResponse>(200)]
    public Task<IActionResult> GetReview(Guid mapId, Guid nodeId, Guid flashcardId, Guid reviewId,
        [FromServices] GetFlashcardReview getReview, CancellationToken ct) => Execute(async () =>
    {
        var result = await getReview.ExecuteAsync(mapId, nodeId, flashcardId, reviewId, ct);
        return (result.Status, result.View is null ? null : FlashcardResponseMapping.ToReviewResponse(result.View));
    }, ct);

    private async Task<IActionResult> Execute(Func<Task<(FlashcardStatus Status, object? Body)>> action,
        CancellationToken ct, bool created = false, Guid mapId = default, Guid nodeId = default)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var (status, body) = await action();
            if (status == FlashcardStatus.Success)
            {
                if (created && body is FlashcardResponse card)
                {
                    Response.Headers.Location = $"/api/maps/{mapId:D}/nodes/{nodeId:D}/flashcards/{card.Id:D}";
                    return StatusCode(201, card);
                }
                return body is null ? NoContent() : Ok(body);
            }
            if (status == FlashcardStatus.AuthenticationRequired) await sessions.LogoutAsync(ct);
            return status switch
            {
                FlashcardStatus.AuthenticationRequired => Error(401, "authentication_required", "Autenticação necessária."),
                FlashcardStatus.NotFound => Error(404, "flashcard_not_found", "Recurso não encontrado."),
                FlashcardStatus.InvalidInput => Error(400, "validation_failed", "Confira os campos informados."),
                FlashcardStatus.ContentChanged => Error(409, "flashcard_changed", "O flashcard foi alterado. Busque-o novamente."),
                FlashcardStatus.ReviewIdConflict => Error(409, "review_id_conflict", "O identificador de review já foi usado."),
                _ => throw new InvalidOperationException("Unexpected flashcard result.")
            };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError("Flashcard operation failed due to an unexpected operational error.");
            return Error(500, "unexpected_error", "Não foi possível concluir a operação.");
        }
    }

    private static ObjectResult Error(int status, string code, string title) => new(
        new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } }) { StatusCode = status };
}
