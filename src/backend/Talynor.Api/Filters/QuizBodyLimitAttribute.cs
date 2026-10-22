using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Talynor.Api.Filters;

// Same bounded pre-binding pattern as Flashcards; authoring defaults to 256 KiB,
// while the submission action explicitly supplies its independent 16 KiB cap.
public sealed class QuizBodyLimitAttribute(int limit = QuizBodyLimitAttribute.Limit) : Attribute, IAsyncResourceFilter
{
    public const int Limit = 256 * 1024;
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        request.EnableBuffering(bufferThreshold: limit + 1, bufferLimit: limit + 1);
        var buffer = new byte[8192];
        var total = 0;
        try
        {
            while (total <= limit)
            {
                var read = await request.Body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - total)),
                    context.HttpContext.RequestAborted);
                if (read == 0) break;
                total += read;
            }
        }
        catch (BadHttpRequestException error) when (error.StatusCode == StatusCodes.Status413PayloadTooLarge)
        { total = limit + 1; }
        if (total > limit)
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = 413, Title = "Solicitação muito grande.", Extensions = { ["code"] = "validation_failed" }
            }) { StatusCode = 413 };
            return;
        }
        request.Body.Position = 0;
        await next();
    }
}
