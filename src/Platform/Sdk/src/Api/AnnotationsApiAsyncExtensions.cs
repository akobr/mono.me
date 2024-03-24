using System;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Sdk.Client;
using _42.Platform.Sdk.Model;

namespace _42.Platform.Sdk.Api;

public static class AnnotationsApiAsyncExtensions
{
    public static async Task<ApiResponse<Annotation>> GetAnnotationWithHttpInfoSafeAsync(
        this IAnnotationsApiAsync api,
        string organizationName,
        string projectName,
        string viewName,
        string annotationKey)
    {
        try
        {
            return await api.GetAnnotationWithHttpInfoAsync(
                organizationName,
                projectName,
                viewName,
                annotationKey);
        }
        catch (ApiException exception)
        {
            if (exception.ErrorCode == 404)
            {
                return new ApiResponse<Annotation>(HttpStatusCode.NotFound, null);
            }

            if (exception.ErrorCode == 401)
            {
                throw new UnauthorizedAccessException("Unauthorized access.", exception);
            }

            throw;
        }
    }

    public static async Task<ApiResponse<object>> DeleteAnnotationWithHttpInfoSafeAsync(
        this IAnnotationsApiAsync api,
        string organizationName,
        string projectName,
        string viewName,
        string key)
    {
        try
        {
            return await api.DeleteAnnotationWithHttpInfoAsync(
                organizationName,
                projectName,
                viewName,
                key);
        }
        catch (ApiException exception)
        {
            if (exception.ErrorCode == 404)
            {
                return new ApiResponse<object>(HttpStatusCode.NotFound, null);
            }

            if (exception.ErrorCode == 401)
            {
                throw new UnauthorizedAccessException("Unauthorized access.", exception);
            }

            throw;
        }
    }
}
