// The hand-written halves of the generated clients stay together, as ApiSdk.g.cs keeps their generated halves.
#pragma warning disable SA1402 // File may only contain a single type
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Sdk;

public partial class AccessApiClient
{
    private ISdkConfiguration? _configuration;

    [ActivatorUtilitiesConstructor]
    public AccessApiClient(HttpClient httpClient, ISdkConfiguration configuration)
        : this(httpClient)
    {
        _configuration = configuration;
        BaseUrl = configuration.BaseUrl;
    }

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
        => SdkRequestHelper.ApplyAuthorization(_configuration, request);
}

public partial class AnnotationsApiClient
{
    private ISdkConfiguration? _configuration;

    [ActivatorUtilitiesConstructor]
    public AnnotationsApiClient(HttpClient httpClient, ISdkConfiguration configuration)
        : this(httpClient)
    {
        _configuration = configuration;
        BaseUrl = configuration.BaseUrl;
    }

    // Annotation values keep their date-like strings as stored; typed dates (ValidFrom, stamps) still parse.
    static partial void UpdateJsonSerializerSettings(JsonSerializerSettings settings)
        => settings.DateParseHandling = DateParseHandling.None;

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
        => SdkRequestHelper.ApplyAuthorization(_configuration, request);
}

public partial class ConfigurationsApiClient
{
    // The API writes configuration content of up to 128 levels, and a response holds it a few levels down (the
    // composition read in a list of nodes); Newtonsoft's default of 64 levels would fail where ?format= succeeds.
    private const int MaxDepth = 256;

    private ISdkConfiguration? _configuration;

    [ActivatorUtilitiesConstructor]
    public ConfigurationsApiClient(HttpClient httpClient, ISdkConfiguration configuration)
        : this(httpClient)
    {
        _configuration = configuration;
        BaseUrl = configuration.BaseUrl;
    }

    // Configuration documents keep their date-like strings as stored; typed dates (AuditStamp.At) still parse.
    static partial void UpdateJsonSerializerSettings(JsonSerializerSettings settings)
    {
        settings.DateParseHandling = DateParseHandling.None;
        settings.MaxDepth = MaxDepth;
    }

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
        => SdkRequestHelper.ApplyAuthorization(_configuration, request);
}

public partial class SchemasApiClient
{
    private ISdkConfiguration? _configuration;

    [ActivatorUtilitiesConstructor]
    public SchemasApiClient(HttpClient httpClient, ISdkConfiguration configuration)
        : this(httpClient)
    {
        _configuration = configuration;
        BaseUrl = configuration.BaseUrl;
    }

    // Schema documents keep their date-like strings as stored; typed dates (the stamps) still parse.
    static partial void UpdateJsonSerializerSettings(JsonSerializerSettings settings)
        => settings.DateParseHandling = DateParseHandling.None;

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
        => SdkRequestHelper.ApplyAuthorization(_configuration, request);
}

public partial class TemplatesApiClient
{
    private ISdkConfiguration? _configuration;

    [ActivatorUtilitiesConstructor]
    public TemplatesApiClient(HttpClient httpClient, ISdkConfiguration configuration)
        : this(httpClient)
    {
        _configuration = configuration;
        BaseUrl = configuration.BaseUrl;
    }

    // Template documents keep their date-like strings as stored; typed dates (the stamps) still parse.
    static partial void UpdateJsonSerializerSettings(JsonSerializerSettings settings)
        => settings.DateParseHandling = DateParseHandling.None;

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
        => SdkRequestHelper.ApplyAuthorization(_configuration, request);
}

public partial class KeyValuesApiClient
{
    private ISdkConfiguration? _configuration;

    [ActivatorUtilitiesConstructor]
    public KeyValuesApiClient(HttpClient httpClient, ISdkConfiguration configuration)
        : this(httpClient)
    {
        _configuration = configuration;
        BaseUrl = configuration.BaseUrl;
    }

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
        => SdkRequestHelper.ApplyAuthorization(_configuration, request);
}

public partial class TransfersApiClient
{
    private ISdkConfiguration? _configuration;

    [ActivatorUtilitiesConstructor]
    public TransfersApiClient(HttpClient httpClient, ISdkConfiguration configuration)
        : this(httpClient)
    {
        _configuration = configuration;
        BaseUrl = configuration.BaseUrl;
    }

    // Transferred documents and values keep their date-like strings as stored; typed dates still parse.
    static partial void UpdateJsonSerializerSettings(JsonSerializerSettings settings)
        => settings.DateParseHandling = DateParseHandling.None;

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
        => SdkRequestHelper.ApplyAuthorization(_configuration, request);
}

internal static class SdkRequestHelper
{
    internal static void ApplyAuthorization(ISdkConfiguration? configuration, HttpRequestMessage request)
    {
        var token = configuration?.AccessTokenFactory?.Invoke();

        // Independent of the token: a certificate-only machine sends no Authorization header.
        ApplyClientCertificateHeader(configuration, request);

        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        const string apiKeyPrefix = "ApiKey ";
        request.Headers.Authorization = token.StartsWith(apiKeyPrefix, StringComparison.Ordinal)
            ? new AuthenticationHeaderValue("ApiKey", token[apiKeyPrefix.Length..])
            : new AuthenticationHeaderValue("Bearer", token);
    }

    private static void ApplyClientCertificateHeader(ISdkConfiguration? configuration, HttpRequestMessage request)
    {
        var certificate = configuration?.ClientCertificate;
        var headerName = configuration?.ClientCertificateHeaderName;

        if (certificate is null || string.IsNullOrEmpty(headerName))
        {
            return;
        }

        request.Headers.Remove(headerName);
        request.Headers.TryAddWithoutValidation(headerName, Convert.ToBase64String(certificate.RawData));
    }
}
