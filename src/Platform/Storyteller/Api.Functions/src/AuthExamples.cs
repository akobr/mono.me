using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

using HttpTrigger = Microsoft.Azure.Functions.Worker.HttpTriggerAttribute;

namespace _42.Platform.Storyteller.Api;

public static class AuthExamples
{
    [OpenApiOperation(operationId: "greeting-unsecured", tags: new[] { "greeting" }, Summary = "Greetings", Description = "This shows a welcome message.", Visibility = OpenApiVisibilityType.Important)]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "text/plain", bodyType: typeof(string), Summary = "The response", Description = "This returns the response")]
    [Function("http-greeting-unsecured")]
    public static Task<IActionResult> UnsecuredGreetingRun(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/auth/greeting-unsecured")]
        HttpRequestData req,
        FunctionContext executionContext)
    {
        var result = new OkObjectResult("Hello, World from secured channel!");
        return Task.FromResult<IActionResult>(result);
    }

    [OpenApiOperation(operationId: "greeting", tags: new[] { "greeting" }, Summary = "Greetings", Description = "This shows a welcome message.", Visibility = OpenApiVisibilityType.Important)]
    [OpenApiSecurity("function_key", SecuritySchemeType.ApiKey, Name = "code", In = OpenApiSecurityLocationType.Query)]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "text/plain", bodyType: typeof(string), Summary = "The response", Description = "This returns the response")]
    [Function("http-greeting-secured")]
    public static Task<IActionResult> SecuredGreetingRun(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "v1/auth/greeting-secured")]
        HttpRequestData req,
        FunctionContext executionContext)
    {
        var result = new OkObjectResult("Hello, World from secured channel!");
        return Task.FromResult<IActionResult>(result);
    }

    [OpenApiOperation(operationId: "oauth.flows.easyauth", tags: new[] { "oauth" }, Summary = "OAuth easy auth flows - MUST be deployed to Azure", Description = "This shows the OAuth easy auth flows. To use this feature, this function app MUST be deployed to Azure.", Visibility = OpenApiVisibilityType.Important)]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(IEnumerable<string>), Summary = "successful operation", Description = "successful operation")]
    [Function("http-easy-auth")]
    public static Task<IActionResult> EasyAuthRun(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "v1/auth/claims")]
        HttpRequestData req,
        FunctionContext executionContext)
    {
        var claims = req.Identities.First().Claims.Select(p => p.ToString());
        var result = new OkObjectResult(claims);
        return Task.FromResult<IActionResult>(result);
    }
}
