using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.Models;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

using HttpRequestData = Microsoft.Azure.Functions.Worker.Http.HttpRequestData;

namespace _42.Platform.Storyteller.Api.ConditionalWrites;

// The preconditions of an annotation write (RFC 9110 §13.1.1 and §13.1.2), over every field line of If-Match and
// If-None-Match. Unlike a conditional read, a malformed header is refused: ignoring it would make an intended
// conditional write unconditional. If-Match compares strongly, so a weak tag never matches; If-None-Match compares
// weakly. A tag that is not an annotation tag stays in the list as nothing, so it matches no version.
public static class WriteConditions
{
    public static bool TryRead(HttpRequestData request, out AnnotationWriteCondition condition, out IActionResult? error)
    {
        condition = AnnotationWriteCondition.None;
        error = null;

        if (!TryReadMatch(request, Definitions.Parameters.IfMatch, allowWeak: false, out var ifMatch)
            || !TryReadMatch(request, Definitions.Parameters.IfNoneMatch, allowWeak: true, out var ifNoneMatch))
        {
            error = new BadRequestObjectResult(new ErrorResponse("If-Match and If-None-Match take * or a list of entity tags, such as the ETag of a read.")
            {
                ErrorCode = ErrorCodes.InvalidPrecondition,
            });
            return false;
        }

        condition = new AnnotationWriteCondition { IfMatch = ifMatch, IfNoneMatch = ifNoneMatch };
        return true;
    }

    // match: null without the header. False for a header that is neither * nor a strict list of entity tags, or that
    // mixes * with tags.
    private static bool TryReadMatch(HttpRequestData request, string header, bool allowWeak, out AnnotationVersionMatch? match)
    {
        match = null;

        if (!request.Headers.TryGetValues(header, out var values))
        {
            return true;
        }

        var lines = values.ToList();

        if (lines.All(string.IsNullOrWhiteSpace)
            || !EntityTagHeaderValue.TryParseStrictList(lines, out var tags)
            || tags.Count == 0)
        {
            return false;
        }

        var any = tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any));

        if (any)
        {
            if (tags.Count > 1)
            {
                return false;
            }

            match = AnnotationVersionMatch.Anything;
            return true;
        }

        // a tag that names no marker matches nothing, but keeps the list a list: If-Match of only such tags fails
        var markers = tags
            .Select(tag => AnnotationEntityTags.GetMarker(tag, allowWeak))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        match = new AnnotationVersionMatch(false, markers);
        return true;
    }
}
