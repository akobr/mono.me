using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;

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

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
        => SdkRequestHelper.ApplyAuthorization(_configuration, request);
}

public partial class ConfigurationsApiClient
{
    private ISdkConfiguration? _configuration;

    [ActivatorUtilitiesConstructor]
    public ConfigurationsApiClient(HttpClient httpClient, ISdkConfiguration configuration)
        : this(httpClient)
    {
        _configuration = configuration;
        BaseUrl = configuration.BaseUrl;
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
