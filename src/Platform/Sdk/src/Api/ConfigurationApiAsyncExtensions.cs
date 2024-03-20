using System.Net;
using System.Threading.Tasks;
using _42.Platform.Sdk.Client;

namespace _42.Platform.Sdk.Api;

public static class ConfigurationApiAsyncExtensions
{
    public static async Task<ApiResponse<object>> DeleteConfigurationWithHttpInfoSafeAsync(this IConfigurationApiAsync api, string organization, string project, string view, string key)
    {
        try
        {
            return await api.DeleteConfigurationWithHttpInfoAsync(organization, project, view, key);
        }
        catch (ApiException exception)
        {
            if (exception.ErrorCode == 404)
            {
                return new ApiResponse<object>(HttpStatusCode.NotFound, null);
            }

            throw;
        }
    }
}
