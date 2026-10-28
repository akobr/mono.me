using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Conformance;

// The answers of the platform in the errors and refresh vectors, and what a provider reports about them.
internal static class VectorResponses
{
    // A json body goes out as compact JSON, a text body as text/plain; the headers as written. A new message for every
    // request, as the response answers every request of a read.
    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond(JToken response)
    {
        return (_, _) => Task.FromResult(Create((JObject)response));
    }

    // A platform that does not answer: the read ends at the provider's read timeout.
    public static async Task<HttpResponseMessage> NeverAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("A timeout step never answers.");
    }

    public static StorytellerReadMode ModeOf(JObject item)
    {
        return item.Value<string>("read") switch
        {
            "effective" => StorytellerReadMode.Effective,
            "resolved" => StorytellerReadMode.Resolved,
            var read => throw new InvalidOperationException($"The read {read} is not one this runner implements."),
        };
    }

    // The keys of IConfiguration with a value, by exact text.
    public static Dictionary<string, string> KeysOf(IConfiguration configuration)
    {
        return configuration.AsEnumerable()
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);
    }

    public static Dictionary<string, string> ExpectedKeys(JToken keys)
    {
        return ((JObject)keys).Properties().ToDictionary(property => property.Name, property => property.Value.Value<string>()!, StringComparer.Ordinal);
    }

    public static void ShouldBeFailure(this StorytellerConfigurationException exception, JToken expected)
    {
        exception.StatusCode.ShouldBe(expected.Value<int?>("status"), exception.Message);
        exception.ErrorCode.ShouldBe(expected.Value<string?>("errorCode"), exception.Message);
        exception.IsTransient.ShouldBe(expected.Value<bool>("transient"), exception.Message);
    }

    // effective reads …/configuration/{key}, resolved …/configuration/{key}/resolved.
    public static void ShouldGoToTheModePath(this RecordedRequest request, StorytellerReadMode mode)
    {
        request.Uri.AbsolutePath.EndsWith("/resolved", StringComparison.Ordinal).ShouldBe(mode == StorytellerReadMode.Resolved, request.Uri.AbsolutePath);
        request.Uri.AbsolutePath.ShouldContain("/configuration/");
    }

    private static HttpResponseMessage Create(JObject response)
    {
        var message = new HttpResponseMessage((HttpStatusCode)response.Value<int>("status"));

        if (response["json"] is { } json)
        {
            message.Content = new StringContent(json.ToString(Formatting.None), Encoding.UTF8, "application/json");
        }
        else if (response["text"] is { } text)
        {
            message.Content = new StringContent(text.Value<string>()!, Encoding.UTF8, "text/plain");
        }

        foreach (var header in (response["headers"] as JObject)?.Properties() ?? [])
        {
            var value = header.Value.Value<string>();

            if (!message.Headers.TryAddWithoutValidation(header.Name, value))
            {
                message.Content ??= new ByteArrayContent([]);
                message.Content.Headers.TryAddWithoutValidation(header.Name, value);
            }
        }

        return message;
    }
}
