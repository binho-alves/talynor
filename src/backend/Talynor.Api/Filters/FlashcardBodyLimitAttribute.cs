using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Talynor.Api.Filters;

// Endpoint-only defense before JSON binding, including streams without Content-Length.
// Retains at most limit+1 bytes in memory; no temporary files or unbounded buffering.
public sealed class FlashcardBodyLimitAttribute : Attribute, IAsyncResourceFilter
{
    public const int Limit = 64 * 1024;

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
        {
            total = Limit + 1; // Native Kestrel enforcement uses the same sanitized result.
        }
        if (total > Limit)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge, Title = "Solicitação muito grande.",
                Extensions = { ["code"] = "validation_failed" }
            }) { StatusCode = StatusCodes.Status413PayloadTooLarge };
            return;
        }
        request.Body.Position = 0;
        await next();
    }
}
