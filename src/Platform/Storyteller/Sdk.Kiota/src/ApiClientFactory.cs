using System.Net.Http;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace ApiSdk;

public class ApiClientFactory(
    HttpClient httpClient,
    IAuthenticationProvider authenticationProvider)
{
    public ApiClient Create()
    {
        var adapter = new HttpClientRequestAdapter(
            authenticationProvider,
            httpClient: httpClient);

        return new ApiClient(adapter);
    }
}
