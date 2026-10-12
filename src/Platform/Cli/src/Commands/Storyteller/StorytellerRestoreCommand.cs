using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.RESTORE, Description = "Restore deleted annotations, with what their deletion removed below them.")]
public class StorytellerRestoreCommand : BaseContextCommand
{
    // the API accepts at most this many keys in one request
    private const int MaxKeysPerRequest = 100;

    private readonly IAnnotationsApiClient _annotationsApi;

    public StorytellerRestoreCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAnnotationsApiClient annotationsApi)
        : base(console, context)
    {
        _annotationsApi = annotationsApi;
    }

    [Argument(0, Description = "The keys of the annotations to restore.")]
    public string[] AnnotationKeys { get; set; } = [];

    [Option("--without-descendants", CommandOptionType.NoValue, Description = "Restore only the given annotations; their deleted descendants stay deleted.")]
    public bool WithoutDescendants { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var keys = new List<string>();

        foreach (var rawKey in AnnotationKeys.Distinct())
        {
            keys.Add(Console.ValidateAnnotationKey(rawKey).ToString());
        }

        if (keys.Count == 0)
        {
            Console.WriteImportant("Please specify at least one annotation key.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var restored = new List<Annotation>();
        var created = new HashSet<string>();
        var failed = new List<AnnotationKeyFailure>();

        foreach (var chunk in keys.Chunk(MaxKeysPerRequest))
        {
            AnnotationRestoreResult result;

            try
            {
                result = await _annotationsApi.RestoreAnnotationsAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    new AnnotationRestoreRequest { Keys = chunk.ToList(), Descendants = !WithoutDescendants });
            }
            catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.BadRequest)
            {
                // the earlier requests may have restored keys already
                WriteRestored(restored, created);
                WriteFailed(failed);
                Console.WriteLine(e.GetErrorMessage());
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            restored.AddRange(result.Annotations ?? []);
            created.UnionWith(result.CreatedKeys ?? []);
            failed.AddRange(result.Failed ?? []);
        }

        WriteRestored(restored, created);
        WriteFailed(failed);

        return failed.Count > 0 ? ExitCodes.ERROR_WRONG_INPUT : ExitCodes.SUCCESS;
    }

    private void WriteRestored(IReadOnlyList<Annotation> restored, IReadOnlySet<string> created)
    {
        if (restored.Count == 0)
        {
            Console.WriteLine("Nothing was restored.".ThemedLowlight(Console.Theme));
            return;
        }

        Console.WriteHeader("Restored");

        foreach (var annotation in restored)
        {
            Console.WriteLine(created.Contains(annotation.AnnotationKey)
                ? $"- {annotation.AnnotationKey} (created as a missing ancestor)"
                : $"- {annotation.AnnotationKey}");
        }

        Console.WriteLine();
    }

    private void WriteFailed(IReadOnlyList<AnnotationKeyFailure> failed)
    {
        if (failed.Count == 0)
        {
            return;
        }

        Console.WriteHeader("Not restored");

        foreach (var failure in failed)
        {
            Console.WriteLine($"- {failure.Key}: {failure.Message}");
        }

        Console.WriteLine();
    }
}
