using System.Net;
using _42.Platform.Storyteller.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker.Http;

namespace _42.Platform.Storyteller.Api.V1;

internal static class DiffResultHttpExtensions
{
    public static async Task<IActionResult> ToDiffResponseAsync(this Task<DiffResult> diffTask, HttpRequestData request)
    {
        DiffResult diff;

        try
        {
            diff = await diffTask;
        }
        catch (InvalidOperationException ex)
        {
            // one of the compared versions (or views) doesn't exist
            return new NotFoundObjectResult(new ErrorResponse(ex.Message));
        }

        return diff.ToDiffResponse(request);
    }

    public static IActionResult ToDiffResponse(this DiffResult diff, HttpRequestData request)
    {
        var format = request.Query[Definitions.Parameters.Format];

        if (string.Equals(format, "unified", StringComparison.OrdinalIgnoreCase))
        {
            return new ContentResult
            {
                Content = DiffFormatter.ToUnifiedDiff(diff),
                ContentType = Definitions.ContentTypes.PlainText,
                StatusCode = (int)HttpStatusCode.OK,
            };
        }

        return new OkObjectResult(diff);
    }
}
