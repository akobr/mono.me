using System.Net;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Services;

public static class AnnotationsApiExtensions
{
    /// <summary>
    /// Gets an active annotation.
    /// </summary>
    /// <returns>The annotation, or null when it doesn't exist or is deleted.</returns>
    public static async Task<Annotation?> TryGetAnnotationAsync(
        this IAnnotationsApiClient @this,
        string organization,
        string project,
        string view,
        string annotationKey)
    {
        try
        {
            return await @this.GetAnnotationAsync(organization, project, view, annotationKey);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets a deleted annotation, with its <see cref="Annotation.Deletion"/>.
    /// </summary>
    /// <returns>The annotation, or null when it isn't deleted (active, purged, or unknown).</returns>
    public static async Task<Annotation?> TryGetDeletedAnnotationAsync(
        this IAnnotationsApiClient @this,
        string organization,
        string project,
        string view,
        string annotationKey)
    {
        try
        {
            return await @this.GetDeletedAnnotationAsync(organization, project, view, annotationKey);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// The stable error code of an API error (for example <c>AnnotationNotDeleted</c>), when the response has one.
    /// </summary>
    public static string? GetErrorCode(this ApiException @this)
    {
        return @this is ApiException<ErrorResponse> error ? error.Result?.ErrorCode : null;
    }

    /// <summary>
    /// The message of an API error for the user: the server's message when the response has one.
    /// </summary>
    public static string GetErrorMessage(this ApiException @this)
    {
        return @this is ApiException<ErrorResponse> error && !string.IsNullOrWhiteSpace(error.Result?.Message)
            ? error.Result.Message
            : @this.Message;
    }
}
