using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// Requests and responses of the anonymous OAuth routes: the user token endpoint and the discovery of the issuer.
internal static class OAuthHttpTesting
{
    public const string FormContentType = "application/x-www-form-urlencoded";

    public static string Form(IEnumerable<(string Name, string Value)> parameters)
    {
        return string.Join('&', parameters.Select(parameter => $"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value)}"));
    }

    // A form POST; a null content type sends none.
    public static TestHttpRequestData Post(string url, string body, string? contentType = FormContentType, IDictionary<string, string>? headers = null)
    {
        var allHeaders = new Dictionary<string, string>(headers ?? new Dictionary<string, string>());

        if (contentType is not null)
        {
            allHeaders["Content-Type"] = contentType;
        }

        return Create(url, allHeaders, "POST", Encoding.UTF8.GetBytes(body));
    }

    public static TestHttpRequestData Get(string url, IDictionary<string, string>? headers = null)
    {
        return Create(url, headers, "GET", null);
    }

    public static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseData response)
    {
        var body = response.Body;
        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body);
        return (response.StatusCode, document.RootElement.Clone());
    }

    public static string Header(HttpResponseData response, string name)
    {
        return string.Join(", ", response.Headers.GetValues(name));
    }

    public static List<string> PropertyNames(JsonElement element)
    {
        return element.EnumerateObject().Select(property => property.Name).ToList();
    }

    public static List<string?> Strings(JsonElement array)
    {
        return array.EnumerateArray().Select(item => item.GetString()).ToList();
    }

    private static TestHttpRequestData Create(string url, IDictionary<string, string>? headers, string method, byte[]? body)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestFunctionContext(services);
        var request = new TestHttpRequestData(context, headers, method, bodyBytes: body, url: new Uri(url));
        ((TestInvocationFeatures)context.Features).Set<IHttpRequestDataFeature>(new TestHttpRequestFeature(request));
        return request;
    }
}
