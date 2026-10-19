using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http.Features;

namespace Talynor.Api.Filters;

// Only the three payload-free Study Session writes use this filter. Native CSRF runs first.
public sealed class StudySessionNoBodyAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        // Native antiforgery may already have consumed a non-seekable form body.
        // Inspect its cached form without parsing again or requiring Content-Length.
        var form = context.HttpContext.Features.Get<IFormFeature>()?.Form;
        var buffer = new byte[1];
        if (request.ContentLength > 0 || form is { Count: > 0 } || form is { Files.Count: > 0 }
            || await request.Body.ReadAsync(buffer, context.HttpContext.RequestAborted) != 0)
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.Result = new BadRequestObjectResult(new ProblemDetails
            {
                Status = 400, Title = "Esta operação não aceita conteúdo na solicitação.",
                Extensions = { ["code"] = "validation_failed" }
            });
            return;
        }
        await next();
    }
}
