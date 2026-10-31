using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;
using AnnotationKey = _42.Platform.Storyteller.AnnotationKey;
using AnnotationType = _42.Platform.Storyteller.AnnotationType;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.DELETE, CommandNames.REMOVE, Description = "Delete an annotation and its descendants; they can be restored until they are purged.")]
public class StorytellerDeleteCommand : BaseContextCommand
{
    private const string AnnotationNotDeleted = "AnnotationNotDeleted";

    private readonly IAnnotationsApiClient _annotationsApi;

    public StorytellerDeleteCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAnnotationsApiClient annotationsApi)
        : base(console, context)
    {
        _annotationsApi = annotationsApi;
    }

    [Argument(0, Description = "An annotation key to delete.")]
    public string AnnotationKey { get; set; } = string.Empty;

    // -p is taken by --projectKey of every context command
    [Option("--permanent", CommandOptionType.NoValue, Description = "Delete permanently, without a way to restore it (Administrator or Owner).")]
    public bool Permanent { get; set; }

    [Option("-y|--yes", CommandOptionType.NoValue, Description = "Don't ask for confirmation of a permanent delete.")]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var annotationKey = Console.ValidateAnnotationKey(AnnotationKey);

        return Permanent
            ? await DeletePermanentlyAsync(annotationKey)
            : await DeleteAsync(annotationKey);
    }

    private async Task<int> DeleteAsync(AnnotationKey annotationKey)
    {
        AnnotationDeletionResult result;

        try
        {
            result = await _annotationsApi.DeleteAnnotationAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                annotationKey);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            Console.WriteLine($"The annotation '{annotationKey}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.Forbidden)
        {
            // for example a machine without the management scope Annotation.Manage: the server names it
            return Console.WriteApiError(e);
        }

        // a repeated delete returns the existing deletion and deletes nothing
        if (result.DeletedKeys is not { Count: > 0 })
        {
            Console.WriteLine($"The annotation '{annotationKey}' is already deleted ({AuditConsoleExtensions.FormatDeletion(result.Deletion)}).");
            WriteRestoreHint(annotationKey, result.Deletion);
            return ExitCodes.WARNING_NO_WORK_NEEDED;
        }

        Console.WriteLine($"Deleted '{annotationKey}'{FormatDescendants(result.DeletedKeys.Count - 1)}.");
        WriteRestoreHint(annotationKey, result.Deletion);
        return ExitCodes.SUCCESS;
    }

    private async Task<int> DeletePermanentlyAsync(AnnotationKey annotationKey)
    {
        var active = await _annotationsApi.TryGetAnnotationAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, annotationKey);
        var deleted = active is null
            ? await _annotationsApi.TryGetDeletedAnnotationAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, annotationKey)
            : null;

        if (active is null && deleted is null)
        {
            Console.WriteLine($"The annotation '{annotationKey}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        if (!Yes)
        {
            var descendants = active is not null
                ? await DescribeActiveDescendantsAsync(annotationKey)
                : DescribeDeletedDescendants(deleted!.Deletion);

            var isConfirmed = Console.Confirm(new ConfirmOptions
            {
                Message = $"Delete '{annotationKey}'{descendants} permanently? It can't be restored",
                DefaultValue = false,
            });

            if (!isConfirmed)
            {
                Console.WriteLine("Nothing was deleted.");
                return ExitCodes.WARNING_ABORTED;
            }
        }

        AnnotationPurgeResult result;

        try
        {
            result = await PurgeAsync(annotationKey);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.Forbidden)
        {
            Console.WriteLine("Only an Administrator or Owner can delete permanently.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            Console.WriteLine($"The annotation '{annotationKey}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var purged = result.PurgedKeys?.Count ?? 1;
        Console.WriteLine($"Deleted '{annotationKey}'{FormatDescendants(purged - 1)} permanently.");
        return ExitCodes.SUCCESS;
    }

    // The permanent delete is tried first, so a caller without the role changes nothing; an active key is soft
    // deleted only when the server asks for it.
    private async Task<AnnotationPurgeResult> PurgeAsync(AnnotationKey annotationKey)
    {
        try
        {
            return await _annotationsApi.PurgeAnnotationAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, annotationKey);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.Conflict && e.GetErrorCode() == AnnotationNotDeleted)
        {
            var deletion = await _annotationsApi.DeleteAnnotationAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, annotationKey);

            try
            {
                return await _annotationsApi.PurgeAnnotationAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, annotationKey);
            }
            catch (ApiException)
            {
                // the soft delete stays: say so, with the way back, before the failure is reported
                Console.WriteLine($"The annotation '{annotationKey}' was deleted, but not permanently.");
                WriteRestoreHint(annotationKey, deletion.Deletion);
                throw;
            }
        }
    }

    private async Task<string> DescribeActiveDescendantsAsync(AnnotationKey annotationKey)
    {
        if (annotationKey.Type == AnnotationType.UnitOfExecution)
        {
            return string.Empty;
        }

        var response = await _annotationsApi.GetDescendantsAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            annotationKey,
            "all",
            null,
            includeInactive: true);

        var count = response.Annotations?.Count ?? 0;
        var more = string.IsNullOrWhiteSpace(response.ContinuationToken) ? string.Empty : "+";

        return count switch
        {
            0 => string.Empty,
            1 when more.Length == 0 => " and its 1 descendant",
            _ => $" and its {count}{more} descendants",
        };
    }

    // The root of a deletion knows how many descendants it took with it; for any other deleted key the count is unknown.
    private static string DescribeDeletedDescendants(AnnotationDeletion? deletion)
    {
        return deletion is { IsRoot: true, DescendantCount: { } count }
            ? count switch
            {
                0 => string.Empty,
                1 => " and its 1 deleted descendant",
                _ => $" and its {count} deleted descendants",
            }
            : " and its deleted descendants";
    }

    private static string FormatDescendants(int count)
    {
        return count switch
        {
            <= 0 => string.Empty,
            1 => " and 1 descendant",
            _ => $" and {count} descendants",
        };
    }

    private void WriteRestoreHint(AnnotationKey annotationKey, AnnotationDeletion? deletion)
    {
        Console.WriteLine(deletion?.PurgeAt is { } purgeAt
            ? $"Restore it with 'sform story restore {annotationKey}' before {AuditConsoleExtensions.FormatDate(purgeAt)}; it is purged automatically after that."
            : $"Restore it with 'sform story restore {annotationKey}'.");
    }
}
