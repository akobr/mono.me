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
    {
        var token = _configuration?.AccessTokenFactory?.Invoke();

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
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
    {
        var token = _configuration?.AccessTokenFactory?.Invoke();

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
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
    {
        var token = _configuration?.AccessTokenFactory?.Invoke();

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}
