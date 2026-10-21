using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Talynor.Api.Filters;

public sealed class QuizNoBodyAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var form = context.HttpContext.Features.Get<IFormFeature>()?.Form;
        var buffer = new byte[1];
        if (request.ContentLength > 0 || form is { Count: > 0 } || form is { Files.Count: > 0 } ||
            await request.Body.ReadAsync(buffer, context.HttpContext.RequestAborted) != 0)
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
