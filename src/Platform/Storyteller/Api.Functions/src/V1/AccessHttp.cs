using System.Net;
using _42.Platform.Storyteller.Access.Entities;
using _42.Platform.Storyteller.Api.OpenApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.V1;

// roles in DB: Reader, Contributor, Administrator, Owner
// machine scopes: Default.Read, Default.ReadWrite, Annotation.Read, Annotation.ReadWrite, Configuration.Read, Configuration.ReadWrite
// two app registrations in Azure AD : one for read-only, one for read-and-write
public class AccessHttp
{
    public const string AccountTag = "Access";
    public const string AccountInfoRoute = "v1/access/account";
    public const string AccessPointsRoute = "v1/access/points";
    public const string AccessPointRoute = "v1/access/points/{key}";

    public const string GrantAccessRoute = "v1/access/grant";
    public const string RevokeAccessRoute = "v1/access/revoke";

    public const string MachinesRoute = "/v1/{organization}/{project}/access/machines";
    public const string MachineRoute = "v1/{organization}/{project}/access/machines/{id}";

    [Function("GetAccountInfo")]
    [OpenApiOperation(AccountInfoRoute, AccountTag)]
    [OpenApiSecurity("implicit_auth", SecuritySchemeType.OAuth2, Flows = typeof(ImplicitAuthFlow))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Account), Description = "Details about the log in account.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized)]
    public async Task<IActionResult> GetAccountInfoAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = AccountInfoRoute)]
        HttpRequestData request)
    {
        var identities = request.Identities.ToList();
        return new OkObjectResult(identities);
    }

    //[Function("PostAccountInfo")]
    //[OpenApiOperation(AccountInfoRoute, AccountTag)]
    //[OpenApiSecurity("implicit_auth", SecuritySchemeType.OAuth2, Flows = typeof(ImplicitAuthFlow))]
    //[OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Account), Description = "Details about the log in account.")]
    //[OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized)]
    //public async Task<IActionResult> PostAccountInfoAsync(
    //    [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = AccountInfoRoute)]
    //    HttpRequestData request)
    //{
    //    var identities = request.Identities.ToList();
    //    return new OkObjectResult(identities);
    //}

    /*[FunctionName("GetAccessPoints")]
    [OpenApiOperation(AccountInfoRoute, AccountTag)]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiSecurity("implicit_auth", SecuritySchemeType.OAuth2, Flows = typeof(ImplicitAuthFlow))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(IReadOnlyDictionary<string, string>), Description = "The list of all available organizations and projects.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized)]
    public async Task<IActionResult> GetAccessPointsAsync()
    {
        // return list of access point where the account can define access rights (is admin or owner)
    }


    [FunctionName("CreateProject")]
    [OpenApiOperation(AccountInfoRoute, AccountTag)]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiSecurity("implicit_auth", SecuritySchemeType.OAuth2, Flows = typeof(ImplicitAuthFlow))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Response), Description = "The list of annotations.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized)]
    public async Task<IActionResult> CreateProjectAsync()
    {

    }

    public async Task<IActionResult> GrantPermissionAsync()
    {

    }

    public async Task<IActionResult> RevokePermissionAsync()
    {

    }

    public async Task<IActionResult> GetMachinesAsync()
    {

    }

    public async Task<IActionResult> GetMachineAsync()
    {

    }

    public async Task<IActionResult> CreateMachineAsync()
    {

    }

    public async Task<IActionResult> ResetMachineAsync()
    {

    }

    public async Task<IActionResult> DeleteMachineAsync()
    {

    }*/
}
