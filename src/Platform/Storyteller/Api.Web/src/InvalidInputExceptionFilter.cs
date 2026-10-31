using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace _42.Platform.Storyteller.Api;

// An InvalidInputException breaks a rule of the request, for example an invalid name query, whether the controller
// parses it or the service hits it while filtering a page: it answers 400 with its message and error code.
public sealed class InvalidInputExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not InvalidInputException exception)
        {
            return;
        }

        context.Result = new BadRequestObjectResult(new { exception.Message, exception.ErrorCode });
        context.ExceptionHandled = true;
    }
}
