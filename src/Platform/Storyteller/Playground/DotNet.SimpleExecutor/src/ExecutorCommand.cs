using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.ConfigurationFormats;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Hosting;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Hosting;

namespace _42.Platform.Storyteller.Playground.DotNet.SimpleExecutor;

// Renders who the process is, the executions in the scope of its key, and their units and units of execution, each with
// its document in a format of ConfigurationFormats. The catalog lists no units, so they come from the UnitNames of the
// executions, and a unit without a unit of execution does not show.
//
// Prints the values of the documents, so run it with a machine without Secrets.Read.
[Command("simple-executor", Description = "Renders who this process is, its executions, their units and units of execution.")]
public sealed class ExecutorCommand : BaseCommand
{
    public const int Succeeded = 0;
    public const int Failed = 1;
    public const int WrongInput = 2;

    private const string All = "all";
    private const string Who = "who";
    private const string Executions = "executions";
    private const string Units = "units";
    private const string UnitsOfExecution = "units-of-execution";

    private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(10);

    private readonly IExecutionContext _self;
    private readonly ISubscriptionCatalog _catalog;
    private readonly IStorytellerDocuments _documents;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ExecutionRenderer _renderer;
    private readonly HashSet<string> _renderedUnits = new(StringComparer.Ordinal);

    private bool _anyDocumentFailed;

    public ExecutorCommand(
        IExtendedConsole console,
        IExecutionContext self,
        ISubscriptionCatalog catalog,
        IStorytellerDocuments documents,
        IHostApplicationLifetime lifetime)
        : base(console)
    {
        _self = self;
        _catalog = catalog;
        _documents = documents;
        _lifetime = lifetime;
        _renderer = new ExecutionRenderer(console.Console.Out);
    }

    [Argument(0, Name = "what", Description = "What to render: all, who, executions, units or units-of-execution.")]
    public string What { get; set; } = All;

    [Option("-c|--context", CommandOptionType.SingleValue, Description = "The context to list, for a key without one (rst, unt, usg).")]
    public string? Context { get; set; }

    [Option("-f|--format", CommandOptionType.SingleValue, Description = "The format of the documents: flat, properties, env or yaml.")]
    public string Format { get; set; } = "yaml";

    [Option("-w|--watch", CommandOptionType.NoValue, Description = "Keep following the executions, with a poll every 10 seconds.")]
    public bool Watch { get; set; }

    public override async Task<int> OnExecuteAsync()
    {
        if (What is not (All or Who or Executions or Units or UnitsOfExecution))
        {
            return Refuse($"Unknown '{What}': use {All}, {Who}, {Executions}, {Units} or {UnitsOfExecution}.");
        }

        if (!ConfigurationFormatNames.TryParse(Format, out var format))
        {
            return Refuse($"Unknown format '{Format}': use {string.Join(", ", ConfigurationFormatNames.Names)}.");
        }

        if (Context is not null && (string.IsNullOrWhiteSpace(Context) || Context.Contains('.')))
        {
            return Refuse("A context is one segment of a key: not empty, and without a dot.");
        }

        var stopping = _lifetime.ApplicationStopping;

        try
        {
            return await RenderAsync(format, stopping);
        }
        catch (Exception) when (stopping.IsCancellationRequested)
        {
            // Ctrl+C. The host stops without waiting for the command, so whatever this unwinding meets does not matter.
            return Succeeded;
        }
        catch (StorytellerConfigurationException exception)
        {
            Console.Console.Error.WriteLine(exception.Message);
            return Failed;
        }
    }

    private async Task<int> RenderAsync(ConfigurationFormat format, CancellationToken stopping)
    {
        var self = await _self.GetAsync(stopping);

        if (What is All or Who)
        {
            _renderer.WriteWho(self, TimeProvider.System.GetUtcNow());
        }

        if (What == Who)
        {
            return Succeeded;
        }

        var key = FullKey.Parse(self.Key).Annotation;

        if (key.ResponsibilityName.Length == 0)
        {
            return Refuse($"{self.Key} names no responsibility: render {Who}, or use a key of rst, unt, usg, exe or uxe.");
        }

        if (key.ContextName.Length > 0 && Context is not null)
        {
            return Refuse($"{self.Key} names its context: leave out --context.");
        }

        // A unit's own document needs no context; a key that names a unit shows it even when no execution has it yet.
        if (key.Type == AnnotationType.Unit && What is All or Units)
        {
            await WriteUnitAsync(key.ResponsibilityName, key.UnitName, format, stopping);

            if (Context is null)
            {
                return Result();
            }
        }

        var context = key.ContextName.Length > 0 ? key.ContextName : Context;

        if (context is null)
        {
            return Refuse($"{self.Key} names no context: add --context.");
        }

        if (key.Type is AnnotationType.Execution or AnnotationType.UnitOfExecution && !Watch)
        {
            return await RenderOneAsync(self, key, format, stopping);
        }

        await RenderEventsAsync(key, context, format, stopping);
        return Result();
    }

    // One execution by its key, in any status. A machine restricted to the execution can read it.
    private async Task<int> RenderOneAsync(StorytellerExecutionContext self, AnnotationKey key, ConfigurationFormat format, CancellationToken stopping)
    {
        var executionKey = AnnotationKey.CreateExecution(key.SubjectName, key.ResponsibilityName, key.ContextName).ToString();
        var execution = await _catalog.GetExecutionAsync(executionKey, stopping);

        if (execution is null)
        {
            Console.Console.Error.WriteLine($"The execution {executionKey} does not exist or is deleted.");
            return Failed;
        }

        await RenderExecutionAsync("Execution", execution, self.Usage, format, stopping);
        return Result();
    }

    // The informer: one poll, or with --watch every change from now on. A key that names a subject keeps its events only.
    private async Task RenderEventsAsync(AnnotationKey key, string context, ConfigurationFormat format, CancellationToken stopping)
    {
        var anyExecution = false;
        var interval = Watch ? WatchInterval : TimeSpan.Zero;

        await foreach (var change in _catalog.WatchExecutionsAsync(key.ResponsibilityName, context, interval, stopping))
        {
            if (key.SubjectName.Length > 0 && !string.Equals(key.SubjectName, change.Execution.SubjectName, StringComparison.Ordinal))
            {
                continue;
            }

            anyExecution = true;

            if (change.Kind == StorytellerChangeKind.Removed)
            {
                _renderer.WriteRemoved(change);
                continue;
            }

            await RenderExecutionAsync(change.Kind.ToString(), change.Execution, change.Usage, format, stopping);
        }

        if (!anyExecution)
        {
            _renderer.WriteNoExecutions(key.ResponsibilityName, context);
        }
    }

    private async Task RenderExecutionAsync(string headline, StorytellerExecution execution, StorytellerAnnotation? usage, ConfigurationFormat format, CancellationToken stopping)
    {
        if (What is All or Executions)
        {
            _renderer.WriteExecution(headline, execution, usage, TimeProvider.System.GetUtcNow());
            await WriteDocumentAsync(execution.AnnotationKey, format, stopping);
        }

        foreach (var unit in execution.UnitNames)
        {
            if (What is All or Units)
            {
                await WriteUnitAsync(execution.ResponsibilityName, unit, format, stopping);
            }

            if (What is All or UnitsOfExecution)
            {
                var unitOfExecution = AnnotationKey.CreateUnitOfExecution(execution.SubjectName, execution.ResponsibilityName, execution.ContextName, unit);
                await WriteDocumentAsync(unitOfExecution.ToString(), format, stopping);
            }
        }
    }

    // Once per run: several subjects share a unit.
    private async Task WriteUnitAsync(string responsibility, string unit, ConfigurationFormat format, CancellationToken stopping)
    {
        if (_renderedUnits.Add(unit))
        {
            await WriteDocumentAsync(AnnotationKey.CreateUnit(responsibility, unit).ToString(), format, stopping);
        }
    }

    // One document that fails does not stop the others.
    private async Task WriteDocumentAsync(string key, ConfigurationFormat format, CancellationToken stopping)
    {
        try
        {
            var document = await _documents.GetAsync(key, stopping);
            _renderer.WriteDocument(key, document.Version, ConfigurationFormatter.Format(document.Content, format));
        }
        catch (Exception exception) when (exception is StorytellerConfigurationException or InvalidDocumentException && !stopping.IsCancellationRequested)
        {
            Console.Console.Error.WriteLine($"! {key}: {exception.Message}");
            _anyDocumentFailed = true;
        }
    }

    private int Result()
    {
        return _anyDocumentFailed ? Failed : Succeeded;
    }

    private int Refuse(string message)
    {
        Console.Console.Error.WriteLine(message);
        return WrongInput;
    }
}
