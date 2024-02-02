using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _42.Platform.Supervisor.Api;

public class SimpleFunctions
{
    [Function(nameof(SyncFunction))]
    public static IActionResult SyncFunction(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)]
        HttpRequestData req,
        FunctionContext context)
    {
        return new OkObjectResult("Hello, World from azure function!");
    }

    [Function(nameof(AsyncFunction))]
    public static async Task<IActionResult> AsyncFunction(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)]
        HttpRequestData req,
        FunctionContext context)
    {
        await Task.Delay(100);
        return new OkObjectResult("Hello, World from azure function!");
    }
}
