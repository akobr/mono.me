using System;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Sdk.Client;
using _42.Platform.Sdk.Model;

namespace _42.Platform.Sdk.Api;

public static class AccessApiAsyncExtensions
{
    public static async Task<ApiResponse<Account>> GetAccountWithHttpInfoSafeAsync(this IAccessApiAsync api)
    {
        try
        {
            return await api.GetAccountWithHttpInfoAsync();
        }
        catch (ApiException exception)
        {
            if (exception.ErrorCode == 404)
            {
                return new ApiResponse<Account>(HttpStatusCode.NotFound, null);
            }

            if (exception.ErrorCode == 401)
            {
                throw new UnauthorizedAccessException("Unauthorized access.", exception);
            }

            throw;
        }
    }
}
