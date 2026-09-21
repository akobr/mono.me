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

public partial class ConfigurationApiClient
{
    private ISdkConfiguration? _configuration;

    [ActivatorUtilitiesConstructor]
    public ConfigurationApiClient(HttpClient httpClient, ISdkConfiguration configuration)
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

        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        const string apiKeyPrefix = "ApiKey ";
        request.Headers.Authorization = token.StartsWith(apiKeyPrefix, StringComparison.Ordinal)
            ? new AuthenticationHeaderValue("ApiKey", token[apiKeyPrefix.Length..])
            : new AuthenticationHeaderValue("Bearer", token);
    }
}
