using _42.Platform.Storyteller.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.V1;

internal static class AnnotationTypeValidation
{
    public static bool TryValidate(string annotationType, ILogger logger, out IActionResult badRequestResult)
    {
        if (AnnotationTypeCodes.ValidCodes.ContainsKey(annotationType))
        {
            badRequestResult = null!;
            return true;
        }

        logger.LogWarning("Invalid request; unknown annotation type '{annotationType}'", annotationType);
        badRequestResult = new BadRequestObjectResult(new ErrorResponse($"Invalid annotation type: {annotationType}"));
        return false;
    }
}
