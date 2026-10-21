using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Talynor.Api.Filters;

// Same bounded pre-binding pattern as Flashcards, scoped only to definition POST/PUT.
public sealed class QuizBodyLimitAttribute : Attribute, IAsyncResourceFilter
{
    public const int Limit = 256 * 1024;
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        request.EnableBuffering(bufferThreshold: Limit + 1, bufferLimit: Limit + 1);
        var buffer = new byte[8192];
        var total = 0;
        try
        {
            while (total <= Limit)
            {
                var read = await request.Body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, Limit + 1 - total)),
                    context.HttpContext.RequestAborted);
                if (read == 0) break;
                total += read;
            }
        }
        catch (BadHttpRequestException error) when (error.StatusCode == StatusCodes.Status413PayloadTooLarge)
        { total = Limit + 1; }
        if (total > Limit)
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
