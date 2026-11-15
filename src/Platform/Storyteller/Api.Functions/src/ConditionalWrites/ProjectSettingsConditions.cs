using System.Globalization;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

using HttpRequestData = Microsoft.Azure.Functions.Worker.Http.HttpRequestData;

namespace _42.Platform.Storyteller.Api.ConditionalWrites;

// The entity tags and the write conditions of project settings. The tag is strong and is the settings' Version in quotes
// ("0" before the first write), so a browser that cannot read ETag across origins builds If-Match from the body. As for
// annotations (WriteConditions), a malformed If-Match is refused rather than ignored, If-Match compares strongly (a weak
// tag never matches) and a tag that is no version stays in the list as nothing. If-None-Match has no meaning for a write:
// the settings always exist.
public static class ProjectSettingsConditions
{
    public static EntityTagHeaderValue EntityTagOf(long version)
    {
        return new EntityTagHeaderValue($"\"{version.ToString(CultureInfo.InvariantCulture)}\"");
    }

    public static bool TryRead(HttpRequestData request, out ProjectSettingsCondition condition, out IActionResult? error)
    {
        condition = ProjectSettingsCondition.None;
        error = null;

        if (request.Headers.Contains(Definitions.Parameters.IfNoneMatch))
        {
            error = InvalidPrecondition("The project settings always exist; a write takes If-Match.");
            return false;
        }

        if (!request.Headers.TryGetValues(Definitions.Parameters.IfMatch, out var values))
        {
            return true;
        }

        var lines = values.ToList();

        if (lines.All(string.IsNullOrWhiteSpace)
            || !EntityTagHeaderValue.TryParseStrictList(lines, out var tags)
            || tags.Count == 0
            || (tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any)) && tags.Count > 1))
        {
            error = InvalidPrecondition("If-Match takes * or a list of entity tags, such as the ETag of a read or the Version of the settings in quotes.");
            return false;
        }

        if (tags[0].Equals(EntityTagHeaderValue.Any))
        {
            condition = ProjectSettingsCondition.Anything;
            return true;
        }

        condition = ProjectSettingsCondition.Of(tags.Select(VersionOf).OfType<long>());
        return true;
    }

    // The version of a strong tag in its canonical spelling ("7", not "07"), as the strong comparison of the tag would
    // decide; null for a weak tag and any other tag.
    private static long? VersionOf(EntityTagHeaderValue tag)
    {
        if (tag.IsWeak)
        {
            return null;
        }

        var value = tag.Tag.Value;

        if (value is not { Length: > 2 } || value[0] != '"' || value[^1] != '"')
        {
            return null;
        }

        var text = value[1..^1];
        var canonical = long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            && version.ToString(CultureInfo.InvariantCulture) == text;
        return canonical ? version : null;
    }

    private static BadRequestObjectResult InvalidPrecondition(string message)
    {
        return new BadRequestObjectResult(new ErrorResponse(message) { ErrorCode = ErrorCodes.InvalidPrecondition });
    }
}
