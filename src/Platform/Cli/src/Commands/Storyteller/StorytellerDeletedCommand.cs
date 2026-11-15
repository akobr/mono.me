using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services.Projects;
using _42.Platform.Storyteller.Sdk;
using Alba.CsConsoleFormat;
using McMaster.Extensions.CommandLineUtils;
using AnnotationKey = _42.Platform.Storyteller.AnnotationKey;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.DELETED, CommandNames.TRASH, Description = "List the deleted annotations, the newest deletion first.")]
public class StorytellerDeletedCommand : BaseContextCommand
{
    private readonly IAnnotationsApiClient _annotationsApi;
    private readonly IProjectSettingsProvider _projectSettings;

    public StorytellerDeletedCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAnnotationsApiClient annotationsApi,
        IProjectSettingsProvider? projectSettings = null)
        : base(console, context)
    {
        _annotationsApi = annotationsApi;
        _projectSettings = projectSettings ?? BuiltInProjectSettings.Instance;
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

        var display = await _projectSettings.GetDisplayAsync();

        if (All)
        {
            Console.WriteTable(
                response.Annotations,
                annotation => new[]
                {
                    CellOf(annotation.AnnotationKey),
                    TypeCell(annotation, display),
                    CellOf(annotation.Deletion?.IsRoot == true ? "yes" : string.Empty),
                    CellOf(FormatDeletedAt(annotation, display)),
                    CellOf(AuditConsoleExtensions.FormatActor(annotation.Deletion?.DeletedBy)),
                    CellOf(FormatDescendants(annotation)),
                    CellOf(FormatPurgeAt(annotation, display)),
                },
                Headers("Key", "Type", "Root", "Deleted", "By", "Descendants", "Purged on"));
        }
        else
        {
            Console.WriteTable(
                response.Annotations,
                annotation => new[]
                {
                    CellOf(annotation.AnnotationKey),
                    TypeCell(annotation, display),
                    CellOf(FormatDeletedAt(annotation, display)),
                    CellOf(AuditConsoleExtensions.FormatActor(annotation.Deletion?.DeletedBy)),
                    CellOf(FormatDescendants(annotation)),
                    CellOf(FormatPurgeAt(annotation, display)),
                },
                Headers("Key", "Type", "Deleted", "By", "Descendants", "Purged on"));
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

    // The type's name (Responsibility, UnitOfExecution, …), or the project's name of it, with its color and icon.
    private static Cell TypeCell(Annotation annotation, ProjectDisplay display)
    {
        return AnnotationKey.TryParse(annotation.AnnotationKey, out var key)
            ? display.TypeCell(key.TypeCode, key.Type.ToString())
            : CellOf(string.Empty);
    }

    // A cell as the toolkit's table of texts makes it.
    private static Cell CellOf(string text)
    {
        return new Cell(text) { Stroke = LineThickness.None, Margin = new Thickness(1, 0) };
    }

    private static IEnumerable<IHeaderColumn> Headers(params string[] headers)
    {
        return headers.Select(header => new HeaderColumn(header));
    }

    private static string FormatDeletedAt(Annotation annotation, ProjectDisplay display)
    {
        return annotation.Deletion is { } deletion ? AuditConsoleExtensions.FormatTime(deletion.DeletedAt, display) : string.Empty;
    }

    private static string FormatDescendants(Annotation annotation)
    {
        return annotation.Deletion?.DescendantCount is { } count ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string FormatPurgeAt(Annotation annotation, ProjectDisplay display)
    {
        return annotation.Deletion?.PurgeAt is { } purgeAt ? AuditConsoleExtensions.FormatDate(purgeAt, display) : string.Empty;
    }
}
