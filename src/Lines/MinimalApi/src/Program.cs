using System.Diagnostics;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var versioning = builder.Services.AddApiVersioning(options =>
{
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
    options.ReportApiVersions = true;
});

versioning.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'V";
    options.SubstituteApiVersionInUrl = true;
});

builder.Services.AddOpenApi("v1");

var app = builder.Build();

app.UseMiddleware<ExceptionHandlerMiddleware>();

//app.Use((context, next) =>
//{
//    try
//    {
//        return next(context);
//    }
//    catch (Exception exception)
//    {
//        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
//        return context.Response.WriteAsJsonAsync(
//            new ProblemDetails {
//                Status = StatusCodes.Status500InternalServerError,
//                Title = "An error occurred while processing your request.",
//                Extensions = { ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier } }
//            (JsonSerializerOptions?)null,
//            "application/problem+json",
//            CancellationToken.None);
//    }
//});

app.MapOpenApi().CacheOutput();
app.MapScalarApiReference();
app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();

await app.RunAsync();
