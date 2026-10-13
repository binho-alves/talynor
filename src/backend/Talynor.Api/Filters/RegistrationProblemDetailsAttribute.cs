using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Talynor.Api.Contracts;

namespace Talynor.Api.Filters;

// Endpoint-only: also runs for native antiforgery and automatic model-validation failures.
public sealed class RegistrationProblemDetailsAttribute : Attribute, IAlwaysRunResultFilter, IOrderedFilter
{
    public int Order => -3000; // Before MVC's client-error result transformation.

    public void OnResultExecuting(ResultExecutingContext context)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        if (context.Result is IAntiforgeryValidationFailedResult)
        {
            context.Result = new BadRequestObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Não foi possível validar a proteção CSRF.",
                Extensions = { ["code"] = "csrf_invalid" }
            });
        }
        else if (context.Result is UnsupportedMediaTypeResult)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status415UnsupportedMediaType,
                Title = "O tipo de conteúdo da solicitação não é suportado.",
                Extensions = { ["code"] = "validation_failed" }
            }) { StatusCode = StatusCodes.Status415UnsupportedMediaType };
        }
        else if (!context.ModelState.IsValid &&
                 context.Result is ObjectResult { Value: ValidationProblemDetails })
        {
            // Never forward JSON parser/conversion diagnostics or attempted values.
            var errors = context.ModelState.Where(entry => entry.Value!.Errors.Count > 0)
                .GroupBy(entry => entry.Key.ToLowerInvariant() is "email" or "password"
                    ? entry.Key.ToLowerInvariant() : "request")
                .ToDictionary(group => group.Key,
                    group => group.SelectMany(entry => entry.Value!.Errors).Select(error =>
                        RegisterRequest.ValidationMessages.Contains(error.ErrorMessage)
                            ? error.ErrorMessage : "Entrada inválida.").Distinct().ToArray());
            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Confira os campos informados.",
                Extensions = { ["code"] = "validation_failed" }
            });
        }
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
