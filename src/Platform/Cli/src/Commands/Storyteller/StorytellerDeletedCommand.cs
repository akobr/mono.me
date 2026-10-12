using System.Globalization;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using AnnotationKey = _42.Platform.Storyteller.AnnotationKey;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.DELETED, CommandNames.TRASH, Description = "List the deleted annotations, the newest deletion first.")]
public class StorytellerDeletedCommand : BaseContextCommand
{
    private readonly IAnnotationsApiClient _annotationsApi;

    public StorytellerDeletedCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAnnotationsApiClient annotationsApi)
        : base(console, context)
    {
        _annotationsApi = annotationsApi;
    }

    [Option("-a|--all", CommandOptionType.NoValue, Description = "List every deleted annotation, not only the keys a delete was requested for.")]
    public bool All { get; set; }

    [Option("-k|--continuation-token", CommandOptionType.SingleValue, Description = "The continuation token for next results page.")]
    public string? ContinuationToken { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var response = await _annotationsApi.GetDeletedAnnotationsAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            roots: !All,
            deletionId: null,
            continuationToken: ContinuationToken);

        Console.WriteHeader("Deleted annotations");

        if (response.Annotations is not { Count: > 0 })
        {
            Console.WriteLine("No annotation is deleted.".ThemedLowlight(Console.Theme));
            return ExitCodes.SUCCESS;
        }

        if (All)
        {
            Console.WriteTable(
                response.Annotations,
                annotation => new[]
                {
                    annotation.AnnotationKey,
                    GetTypeName(annotation),
                    annotation.Deletion?.IsRoot == true ? "yes" : string.Empty,
                    FormatDeletedAt(annotation),
                    AuditConsoleExtensions.FormatActor(annotation.Deletion?.DeletedBy),
                    FormatDescendants(annotation),
                    FormatPurgeAt(annotation),
                },
                new[] { "Key", "Type", "Root", "Deleted", "By", "Descendants", "Purged on" });
        }
        else
        {
            Console.WriteTable(
                response.Annotations,
                annotation => new[]
                {
                    annotation.AnnotationKey,
                    GetTypeName(annotation),
                    FormatDeletedAt(annotation),
                    AuditConsoleExtensions.FormatActor(annotation.Deletion?.DeletedBy),
                    FormatDescendants(annotation),
                    FormatPurgeAt(annotation),
                },
                new[] { "Key", "Type", "Deleted", "By", "Descendants", "Purged on" });
        }

        if (!string.IsNullOrWhiteSpace(response.ContinuationToken))
        {
            Console.WriteLine();
            Console.WriteLine("More deleted annotations available, use continuation token to retrieve next page:".ThemedLowlight(Console.Theme));
            Console.WriteLine($"  {response.ContinuationToken}");
        }

        Console.WriteLine();
        Console.WriteLine("Restore them with 'sform story restore <key>...'.".ThemedLowlight(Console.Theme));
        return ExitCodes.SUCCESS;
    }

    private static string GetTypeName(Annotation annotation)
    {
        return AnnotationKey.TryParse(annotation.AnnotationKey, out var key) ? key.Type.ToString() : string.Empty;
    }

    private static string FormatDeletedAt(Annotation annotation)
    {
        return annotation.Deletion is { } deletion ? AuditConsoleExtensions.FormatTime(deletion.DeletedAt) : string.Empty;
    }

    private static string FormatDescendants(Annotation annotation)
    {
        return annotation.Deletion?.DescendantCount is { } count ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string FormatPurgeAt(Annotation annotation)
    {
        return annotation.Deletion?.PurgeAt is { } purgeAt ? AuditConsoleExtensions.FormatDate(purgeAt) : string.Empty;
    }
}
