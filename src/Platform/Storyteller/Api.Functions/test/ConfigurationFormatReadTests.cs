using System.Text.Json;

using _42.Platform.Storyteller.Api.ConditionalReads;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.ConfigurationFormats;
using _42.Platform.Storyteller.Configuring;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using static _42.Platform.Storyteller.Api.Functions.UnitTests.ConfigurationConditionalReadTests;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// ?format= on the effective and resolved configuration reads (configuration-formats.md, API): the formatted body and
// its media type, the 400 and the 422, and the tags and manifests of each format. ConfigurationConditionalReadTests has
// the reads of the envelope and the fixture both use.
public class ConfigurationFormatReadTests
{
    // The tags of Stored() as the reads sent them before the formats existed; a read of the envelope still sends them.
    private const string EnvelopeEffectiveTag = "W/\"e.gQxnNnUu9dapC1KGjjEAfg\"";
    private const string EnvelopeResolvedTag = "W/\"r.h2w34YdZQcW60rzbTdqniQ\"";
    private const string EnvelopeBoundTag = "W/\"b.Xfth_9BG7ZlPoQKyl0utBw\"";

    // View is also a type of the platform, which hides the imported constant.
    private const string View = ConfigurationConditionalReadTests.View;

    private static readonly FullKey FullKeyOfTheRead = FullKey.Create(AnnotationKey.Parse(Key), Org, Project, View);

    [Theory]
    [InlineData("flat")]
    [InlineData("properties")]
    [InlineData("env")]
    [InlineData("yaml")]
    public async Task GetConfiguration_AFormat_IsTheContentAsAClientFormatsIt(string name)
    {
        var (http, _) = Create(() => Typed());
        var request = WithFormat(Read(), name);

        var result = await http.GetConfiguration(request, Org, Project, View, Key);
        var tag = Headers(request).ETag.ToString();
        var again = WithFormat(Read(tag), name);
        var notModified = await http.GetConfiguration(again, Org, Project, View, Key);

        var expected = ClientFormatted(Typed(), name);
        var body = result.ShouldBeOfType<ContentResult>();
        body.StatusCode.ShouldBe(StatusCodes.Status200OK);
        body.Content.ShouldBe(expected.Text);
        body.ContentType.ShouldBe(expected.MediaType);
        tag.ShouldMatch("^W/\"e\\.[A-Za-z0-9_-]{22}\"$");
        tag.ShouldNotBe(await TagOfAsync(http), "the envelope has a tag of its own");
        notModified.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(StatusCodes.Status304NotModified);

        foreach (var sent in new[] { request, again })
        {
            Headers(sent).ETag.ToString().ShouldBe(tag);
            Headers(sent).CacheControl.ToString().ShouldBe("private, no-cache");
        }
    }

    // The server holds a date token and a decimal where a client reads a string and a double; the body is the client's.
    [Fact]
    public async Task GetConfiguration_Flat_WritesTheTokensOfTheServerAsAClientReadsThem()
    {
        var (http, _) = Create(() => Typed());

        var result = await http.GetConfiguration(WithFormat(Read(), "flat"), Org, Project, View, Key);

        var keys = ReadAsTheSdk(result.ShouldBeOfType<ContentResult>().Content!);
        keys["since"]!.Value<string>().ShouldBe("2026-10-20T12:00:00.123+02:00");
        keys["rate"]!.Value<string>().ShouldBe("1.5");
        keys["db:port"]!.Value<string>().ShouldBe("5432");
        keys["db:hosts:1"]!.Value<string>().ShouldBe("b");
    }

    // r: a document without bindings; d: bindings whose inputs were recorded; b: volatile bindings.
    [Theory]
    [InlineData("flat", true, "r")]
    [InlineData("properties", false, "r")]
    [InlineData("env", true, "d")]
    [InlineData("yaml", false, "d")]
    [InlineData("flat", false, "b")]
    [InlineData("env", true, "b")]
    public async Task GetConfigurationResolved_AFormat_IsTheResolvedContentWithTheHeadersOfTheRead(string name, bool mtls, string kind)
    {
        var (http, _) = kind switch
        {
            "r" => Create(() => Typed()),
            "d" => Create(() => Typed(), Resolve("db-1")),
            _ => Create(() => Typed(), Resolve("db-1"), Volatile),
        };
        using var provider = Manifests();
        var first = WithFormat(Machine(mtls, provider: provider), name);

        var result = await http.GetConfigurationResolved(first, Org, Project, View, Key);
        var tag = Headers(first).ETag.ToString();
        var second = WithFormat(Machine(mtls, ifNoneMatch: tag, provider: provider), name);
        var notModified = await http.GetConfigurationResolved(second, Org, Project, View, Key);

        var resolved = Typed();

        if (kind != "r")
        {
            resolved.Content["host"] = "db-1";
        }

        var expected = ClientFormatted(resolved, name);
        var body = result.ShouldBeOfType<ContentResult>();
        body.Content.ShouldBe(expected.Text);
        body.ContentType.ShouldBe(expected.MediaType);
        tag.ShouldMatch($"^W/\"{kind}\\.[A-Za-z0-9_-]{{22}}\"$");
        notModified.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(StatusCodes.Status304NotModified);

        foreach (var sent in new[] { first, second })
        {
            Headers(sent).ETag.ToString().ShouldBe(tag);
            Headers(sent).CacheControl.ToString().ShouldBe(mtls ? "no-store" : "private, no-cache");
            Headers(sent).ContainsKey(SecretRights.WithheldHeader).ShouldBe(!mtls);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("json")]
    [InlineData("JSON")]
    public async Task ConfigurationReads_NoFormatOrJson_AreTheEnvelopeWithItsTags(string? name)
    {
        var (http, _) = Create(() => Stored());
        var effective = WithFormat(Read(), name);
        var resolved = WithFormat(Machine(mtls: true), name);

        var effectiveResult = await http.GetConfiguration(effective, Org, Project, View, Key);
        var resolvedResult = await http.GetConfigurationResolved(resolved, Org, Project, View, Key);

        effectiveResult.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Configuration>();
        resolvedResult.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Configuration>();
        Headers(effective).ETag.ToString().ShouldBe(EnvelopeEffectiveTag, "the tags of the envelope stay as they were");
        Headers(resolved).ETag.ToString().ShouldBe(EnvelopeResolvedTag);
    }

    [Fact]
    public async Task GetConfiguration_AFormatInAnyCasing_HasOneTag()
    {
        var (http, _) = Create(() => Stored());
        var tags = new List<string>();

        foreach (var name in new[] { "env", "ENV", "Env" })
        {
            var request = WithFormat(Read(), name);
            (await http.GetConfiguration(request, Org, Project, View, Key)).ShouldBeOfType<ContentResult>();
            tags.Add(Headers(request).ETag.ToString());
        }

        tags.Distinct().ShouldHaveSingleItem();
    }

    // Each tag answers 304 for its own format and 200 for every other, the envelope included.
    [Fact]
    public async Task GetConfiguration_EachFormat_HasItsOwnTag()
    {
        var (http, _) = Create(() => Stored());
        var names = new[] { "json", "flat", "properties", "env", "yaml" };
        var tags = new Dictionary<string, string>();

        foreach (var name in names)
        {
            var request = WithFormat(Read(), name);
            await http.GetConfiguration(request, Org, Project, View, Key);
            tags[name] = Headers(request).ETag.ToString();
        }

        tags.Values.Distinct().Count().ShouldBe(names.Length);

        foreach (var (tagged, tag) in tags)
        {
            foreach (var name in names)
            {
                var result = await http.GetConfiguration(WithFormat(Read(tag), name), Org, Project, View, Key);

                if (name == tagged)
                {
                    result.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(StatusCodes.Status304NotModified, $"{tagged} tag, {name} read");
                }
                else
                {
                    result.ShouldNotBeOfType<StatusCodeResult>($"{tagged} tag, {name} read");
                }
            }
        }
    }

    // HttpRequestData.Query joins a repeated parameter with commas; nothing is read before the 400 and no header is set.
    [Theory]
    [InlineData("xml", null)]
    [InlineData("unified", null)]
    [InlineData(" flat", null)]
    [InlineData("1", null)]
    [InlineData("flat,yaml", null)]
    [InlineData("flat", "flat")]
    [InlineData("json", "env")]
    public async Task ConfigurationReads_AnUnknownOrRepeatedFormat_AreAnInvalidQueryBeforeAnyRead(string value, string? repeat)
    {
        var (http, configurations) = Create(() => Stored(), Resolve("db-1"));
        var effective = WithFormat(WithFormat(Read("*"), value), repeat);
        var resolved = WithFormat(WithFormat(Machine(mtls: false, ifNoneMatch: "*"), value), repeat);

        var effectiveError = await Should.ThrowAsync<InvalidInputException>(() => http.GetConfiguration(effective, Org, Project, View, Key));
        var resolvedError = await Should.ThrowAsync<InvalidInputException>(() => http.GetConfigurationResolved(resolved, Org, Project, View, Key));

        foreach (var error in new[] { effectiveError, resolvedError })
        {
            error.ErrorCode.ShouldBe(ErrorCodes.InvalidQuery);
            error.Message.ShouldBe("The query parameter 'format' must be json, flat, properties, env or yaml.");
        }

        configurations.Calls.ShouldBeEmpty();
        ShouldHaveNoReadHeaders(effective);
        ShouldHaveNoReadHeaders(resolved);
    }

    // Formatted before the tags are compared, so If-None-Match: * does not hide the error either.
    [Theory]
    [InlineData("flat", """{ "Db": { "Password": "s3cret" }, "db": { "password": "0th3r" } }""", "case-collision")]
    [InlineData("yaml", """{ "db": { "a:b": "s3cret" } }""", "colon-in-name")]
    [InlineData("properties", """{ "db": { "a[0]": "s3cret" } }""", "bracket-in-name")]
    [InlineData("properties", """{ "list": [ "s3cret", null, "0th3r" ] }""", "array-gap")]
    [InlineData("env", """{ "db": { "pass word": "s3cret" } }""", "invalid-env-name")]
    [InlineData("env", """{ "db": { "$binding": { "expression": "s3cret" } } }""", "invalid-env-name")]
    public async Task ConfigurationReads_ADocumentTheFormatCannotRepresent_IsUnprocessableWithoutHeaders(string name, string json, string rule)
    {
        var (http, _) = Create(() => Stored() with { Content = JObject.Parse(json) });
        var effective = WithFormat(Read("*"), name);
        var resolved = WithFormat(Machine(mtls: false, ifNoneMatch: "*"), name);

        var effectiveResult = await http.GetConfiguration(effective, Org, Project, View, Key);
        var resolvedResult = await http.GetConfigurationResolved(resolved, Org, Project, View, Key);

        var expected = Should.Throw<InvalidDocumentException>(() => ConfigurationFormatter.Format(JObject.Parse(json), Parse(name)));
        var error = new JObject
        {
            ["Message"] = expected.Message,
            ["ErrorCode"] = $"format.{rule}",
            ["Details"] = new JObject { ["Format"] = name, ["Rule"] = rule, ["Path"] = expected.Path },
        };

        foreach (var (result, sent) in new[] { (effectiveResult, effective), (resolvedResult, resolved) })
        {
            var body = Unprocessable(result);
            JToken.DeepEquals(body, error).ShouldBeTrue(body.ToString());
            body.ToString().ShouldNotContain("s3cret");
            body.ToString().ShouldNotContain("0th3r");
            ShouldHaveNoReadHeaders(sent);
        }
    }

    [Fact]
    public async Task GetConfiguration_AFormatOfAMissingConfiguration_IsNotFoundWithoutATag()
    {
        var (http, _) = Create(() => null);
        var request = WithFormat(Read("*"), "env");

        var result = await http.GetConfiguration(request, Org, Project, View, Key);

        result.ShouldBeOfType<NotFoundResult>();
        ShouldHaveNoReadHeaders(request);
    }

    [Fact]
    public async Task GetConfigurationResolved_TheManifestOfAFormat_AnswersThatFormatOnly()
    {
        var (http, configurations) = Create(() => Stored(), Resolve("db-1"));
        using var provider = Manifests();
        var first = WithFormat(Machine(mtls: true, provider: provider), "env");
        await http.GetConfigurationResolved(first, Org, Project, View, Key);
        var tag = Headers(first).ETag.ToString();
        configurations.Calls.Clear();

        var env = WithFormat(Machine(mtls: true, ifNoneMatch: tag, provider: provider), "ENV");
        var envResult = await http.GetConfigurationResolved(env, Org, Project, View, Key);

        tag.ShouldStartWith("W/\"d.");
        envResult.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(StatusCodes.Status304NotModified);
        Headers(env).ETag.ToString().ShouldBe(tag);
        configurations.Calls.ShouldBe(
        [
            nameof(IConfigurationService.GetRawConfigurationAsync),
            nameof(IConfigurationService.HasBindings),
            nameof(IConfigurationService.AreDependenciesUnchangedAsync),
        ]);

        var yaml = WithFormat(Machine(mtls: true, ifNoneMatch: tag, provider: provider), "yaml");
        var envelope = Machine(mtls: true, ifNoneMatch: tag, provider: provider);
        var yamlResult = await http.GetConfigurationResolved(yaml, Org, Project, View, Key);
        var envelopeResult = await http.GetConfigurationResolved(envelope, Org, Project, View, Key);

        yamlResult.ShouldBeOfType<ContentResult>();
        envelopeResult.ShouldBeOfType<OkObjectResult>();
        new[] { tag, Headers(yaml).ETag.ToString(), Headers(envelope).ETag.ToString() }.Distinct().Count().ShouldBe(3);
        configurations.Revalidated.Count.ShouldBe(1, "a manifest of env is not revalidated for another format");
        configurations.Calls.Count(call => call == nameof(IConfigurationService.ResolveConfigurationAsync)).ShouldBe(2);
    }

    // The 422 carries no tag and records no manifest: the next read on the same instance resolves again, metered and
    // audited like the first.
    [Fact]
    public async Task GetConfigurationResolved_ABindingThatBreaksTheFormat_IsUnprocessableAfterTheResolveWithoutAManifest()
    {
        var name = "host";
        var (http, configurations) = Create(
            () => Stored(),
            (configuration, _) =>
            {
                configuration.Content["host"] = new JObject { [name] = "db-1" };
                return configuration;
            });
        using var otherInstance = Manifests();
        using var instance = Manifests();
        var valid = WithFormat(Machine(mtls: true, provider: otherInstance), "flat");
        await http.GetConfigurationResolved(valid, Org, Project, View, Key);
        var tag = Headers(valid).ETag.ToString();

        name = "db:host";
        var broken = WithFormat(Machine(mtls: true, provider: instance), "flat");
        var brokenResult = await http.GetConfigurationResolved(broken, Org, Project, View, Key);
        name = "host";
        var again = WithFormat(Machine(mtls: true, ifNoneMatch: tag, provider: instance), "flat");
        var againResult = await http.GetConfigurationResolved(again, Org, Project, View, Key);

        var error = Unprocessable(brokenResult);
        error["ErrorCode"]!.Value<string>().ShouldBe("format.colon-in-name");
        error["Details"]!["Path"]!.Value<string>().ShouldBe("$.host['db:host']");
        ShouldHaveNoReadHeaders(broken);
        againResult.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(StatusCodes.Status304NotModified, "a d tag is the same on every instance");
        configurations.Revalidated.ShouldBeEmpty("the 422 recorded no manifest");
        configurations.Calls.Count(call => call == nameof(IConfigurationService.ResolveConfigurationAsync)).ShouldBe(3);
    }

    [Fact]
    public void Tags_WithoutARepresentation_AreTheTagsOfTheEnvelope()
    {
        var rights = Rights(canReadSecrets: true, withheld: false);

        ConfigurationEntityTags.RepresentationOf(null).ShouldBeNull();
        ConfigurationEntityTags.ForEffective(FullKeyOfTheRead, Stored()).ToString().ShouldBe(EnvelopeEffectiveTag);
        ConfigurationEntityTags.ForResolved(FullKeyOfTheRead, Stored(), rights).ToString().ShouldBe(EnvelopeResolvedTag);
        ConfigurationEntityTags.ForResolvedBindings(FullKeyOfTheRead, Stored(), rights, new byte[32]).ToString().ShouldBe(EnvelopeBoundTag);
    }

    // The canonical name and the formatter's version: env/0 stands for the env of another version of the formatter.
    [Fact]
    public void Tags_FollowTheRepresentation()
    {
        var rights = Rights(canReadSecrets: true, withheld: false);
        string?[] representations = [null, .. Enum.GetValues<ConfigurationFormat>().Select(format => ConfigurationEntityTags.RepresentationOf(format)), "env/0"];

        var effective = representations.Select(representation => ConfigurationEntityTags.ForEffective(FullKeyOfTheRead, Stored(), representation).ToString()).ToList();
        var resolved = representations.Select(representation => ConfigurationEntityTags.ForResolved(FullKeyOfTheRead, Stored(), rights, representation).ToString()).ToList();
        var bound = representations.Select(representation => ConfigurationEntityTags.ForResolvedBindings(FullKeyOfTheRead, Stored(), rights, new byte[32], representation).ToString()).ToList();

        ConfigurationEntityTags.RepresentationOf(ConfigurationFormat.Env).ShouldBe($"env/{ConfigurationFormatter.Version}");
        effective.Concat(resolved).Concat(bound).Distinct().Count().ShouldBe(representations.Length * 3);
        effective.ShouldAllBe(tag => tag.StartsWith("W/\"e.", StringComparison.Ordinal));
        resolved.ShouldAllBe(tag => tag.StartsWith("W/\"r.", StringComparison.Ordinal));
        bound.ShouldAllBe(tag => tag.StartsWith("W/\"b.", StringComparison.Ordinal));
    }

    // ConfigurationConditionalReadTests pins the order of the parameters; this test, the format parameter itself, the
    // formatted bodies next to the model on the 200, the bodyless 304 and the 422 of a format error.
    [Theory]
    [InlineData(Definitions.RouteIds.Configuration.GetConfiguration, Definitions.Descriptions.ResponseConfigurationFormatInvalid)]
    [InlineData(Definitions.RouteIds.Configuration.GetResolvedConfiguration, Definitions.Descriptions.ResponseResolvedConfigurationInvalid)]
    public async Task Document_FormattedReads_DeclareTheFormatTheirMediaTypesAndTheUnprocessableResponse(string operationId, string unprocessable)
    {
        var json = await OpenApiExportTests.RenderAsync();
        using var document = JsonDocument.Parse(json);

        var operation = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(entry => entry.Value)
            .Single(entry => entry.GetProperty("operationId").GetString() == operationId);

        var format = operation.GetProperty("parameters").EnumerateArray().Single(parameter => parameter.GetProperty("name").GetString() == Definitions.Parameters.Format);
        format.GetProperty("in").GetString().ShouldBe("query");
        (format.TryGetProperty("required", out var required) && required.GetBoolean()).ShouldBeFalse();
        format.GetProperty("schema").GetProperty("type").GetString().ShouldBe("string");
        format.GetProperty("description").GetString().ShouldBe(Definitions.Descriptions.ConfigurationFormat);

        var responses = operation.GetProperty("responses");
        var ok = responses.GetProperty("200");
        var content = ok.GetProperty("content");
        content.EnumerateObject().Select(media => media.Name).ShouldBe([Definitions.ContentTypes.Json, Definitions.ContentTypes.PlainText, Definitions.ContentTypes.Yaml]);
        content.GetProperty(Definitions.ContentTypes.Json).GetProperty("schema").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/Configuration");
        ok.GetProperty("description").GetString()!.ShouldEndWith(Definitions.Descriptions.ConfigurationFormatBody);

        foreach (var (mediaType, name, start) in new[]
                 {
                     (Definitions.ContentTypes.PlainText, "properties", "db.hosts[0]=a\n"),
                     (Definitions.ContentTypes.PlainText, "env", "DB__HOSTS__0=a\n"),
                     (Definitions.ContentTypes.Yaml, "yaml", "db:\n  hosts:\n"),
                 })
        {
            var media = content.GetProperty(mediaType);
            media.GetProperty("schema").GetProperty("type").GetString().ShouldBe("string", mediaType);
            media.GetProperty("examples").GetProperty(name).GetProperty("value").GetString().ShouldStartWith(start, customMessage: name);
        }

        responses.GetProperty("304").TryGetProperty("content", out _).ShouldBeFalse();
        var invalid = responses.GetProperty("422");
        invalid.GetProperty("description").GetString().ShouldBe(unprocessable);
        invalid.GetProperty("content").GetProperty(Definitions.ContentTypes.Json).GetProperty("schema").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/ErrorResponse");
        unprocessable.ShouldContain("format.<rule>");
    }

    [Fact]
    public void ResolvedReadUnprocessable_NamesTheBindingAndTheFormatErrors()
    {
        foreach (var code in new[] { "binding.evaluation", "binding.limit.<kind>", "format.<rule>" })
        {
            Definitions.Descriptions.ResponseResolvedConfigurationInvalid.ShouldContain(code);
        }
    }

    // ?format= as HttpRequestData.Query gives it: absent when null, a repeat joined with a comma.
    private static TestHttpRequestData WithFormat(TestHttpRequestData request, string? value)
    {
        if (value is not null)
        {
            request.Query.Add(Definitions.Parameters.Format, value);
        }

        return request;
    }

    private static void ShouldHaveNoReadHeaders(TestHttpRequestData request)
    {
        var headers = Headers(request);
        headers.ContainsKey(HeaderNames.ETag).ShouldBeFalse();
        headers.ContainsKey(HeaderNames.CacheControl).ShouldBeFalse();
        headers.ContainsKey(SecretRights.WithheldHeader).ShouldBeFalse();
    }

    // The 422 as the API writes it, with the MVC options: PascalCase, without nulls.
    private static JObject Unprocessable(IActionResult result)
    {
        var error = result.ShouldBeOfType<ObjectResult>();
        error.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        return JObject.Parse(FunctionTestDoubles.ToJsonBody(error.Value.ShouldBeOfType<ErrorResponse>()));
    }

    private static ConfigurationFormat Parse(string name)
    {
        ConfigurationFormatNames.TryParse(name, out var format).ShouldBeTrue(name);
        return format;
    }

    // The format of Content as a client computes it from the envelope the API writes.
    private static FormattedConfiguration ClientFormatted(Configuration configuration, string name)
    {
        var envelope = ReadAsTheSdk(FunctionTestDoubles.ToJsonBody(configuration));
        return ConfigurationFormatter.Format(envelope[nameof(Configuration.Content)], Parse(name));
    }

    // JSON as the SDK reads it: date-like strings stay strings, and numbers are doubles.
    private static JObject ReadAsTheSdk(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json))
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
        };

        return JObject.Load(reader);
    }

    // Stored() with the tokens the server holds: Cosmos DB reads a date-like string as a date, and the binding converter
    // reads numbers as decimals.
    private static Configuration Typed()
    {
        return Stored() with
        {
            Content = new JObject
            {
                ["db"] = new JObject { ["hosts"] = new JArray("a", "b"), ["port"] = 5432 },
                ["since"] = new JValue(new DateTimeOffset(2026, 10, 20, 12, 0, 0, 123, TimeSpan.FromHours(2))),
                ["rate"] = new JValue(1.50m),
                ["sender"] = "billing@example.com",
                ["host"] = "@db.host",
            },
        };
    }
}
