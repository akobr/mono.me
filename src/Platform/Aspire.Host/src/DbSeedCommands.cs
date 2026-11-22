using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Aspire.Host;

/// <summary>
/// The dashboard commands that run DbCreator with options: <c>Seed with options…</c> on <c>db-seed</c> and
/// <c>Create with options…</c> on <c>db-creator</c>. The dashboard asks for the options in a dialog; the command keeps
/// them as the pending invocation of its resource, whose arguments and environment are callbacks evaluated at every
/// start, starts the resource and clears them once it has started. The plain Start keeps the defaults.
/// </summary>
internal static class DbSeedCommands
{
    private const string FolderInput = "folder";
    private const string OwnerInput = "owner";
    private const string OrganizationInput = "organization";
    private const string ProjectInput = "project";
    private const string ViewInput = "view";
    private const string DryRunInput = "dry-run";
    private const string AgainInput = "again";
    private const string ForceInput = "force";
    private const string PublicPairsOnlyInput = "public-pairs-only";
    private const string SetInputPrefix = "set-";
    private const string DefaultSet = "showcase";

    private const string BaseAccountInput = "base-account";
    private const string StagingSeedInput = "staging-seed";

    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The arguments of <c>db-seed</c> (<c>seed showcase</c>, with <paramref name="defaultOwner"/> as <c>--owner</c>
    /// when there is no base account) and its <c>Seed with options…</c> command.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithSeedOptions(
        this IResourceBuilder<ProjectResource> dbSeed,
        string dataSetsDirectory,
        string? baseAccountId,
        string? defaultOwner)
    {
        var pending = new PendingInvocation<string[]>();
        dbSeed.WithArgs(context =>
        {
            if (pending.Value is { } arguments)
            {
                foreach (var argument in arguments)
                {
                    context.Args.Add(argument);
                }

                return;
            }

            context.Args.Add("seed");
            context.Args.Add(DefaultSet);
            if (baseAccountId is null && defaultOwner is not null)
            {
                context.Args.Add("--owner");
                context.Args.Add(defaultOwner);
            }
        });

        if (baseAccountId is not null)
        {
            dbSeed.WithEnvironment("baseAccount__id", baseAccountId);
        }

        var sets = Directory.Exists(dataSetsDirectory)
            ? Directory.GetDirectories(dataSetsDirectory)
                .Where(folder => File.Exists(Path.Combine(folder, "set.json")))
                .Select(folder => Path.GetFileName(folder))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [DefaultSet];

        dbSeed.WithCommand(
            name: "seed-with-options",
            displayName: "Seed with options…",
            executeCommand: context => SeedAsync(context, dbSeed.Resource, pending, sets),
            commandOptions: new CommandOptions
            {
                Description = "Runs 2sInitDb seed with the data sets and options you pick.",
                IconName = "DatabaseArrowRight",
                IconVariant = IconVariant.Regular,
                Arguments = CreateSeedInputs(sets, defaultOwner),
                ValidateArguments = context =>
                {
                    var anySet = context.Inputs.Any(input => input.Name.StartsWith(SetInputPrefix, StringComparison.Ordinal) && IsChecked(input.Value));
                    var folder = context.Inputs.FirstOrDefault(input => input.Name == FolderInput);
                    if (!anySet && string.IsNullOrWhiteSpace(folder?.Value))
                    {
                        context.AddValidationError(folder ?? context.Inputs[0], "Pick at least one data set, or give a folder.");
                    }

                    if (folder?.Value is { Length: > 0 } path && !Directory.Exists(path))
                    {
                        context.AddValidationError(folder, $"The folder '{path}' does not exist.");
                    }

                    return Task.CompletedTask;
                },
                UpdateState = IdleOnly,
            });

        return dbSeed;
    }

    /// <summary>
    /// The environment of <c>db-creator</c> (<c>stagingSeed__enabled</c>, <c>baseAccount__id</c>) and its
    /// <c>Create with options…</c> command, whose base account id is prefilled with <paramref name="baseAccountId"/>,
    /// else <paramref name="suggestedBaseAccountId"/>.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithCreateOptions(
        this IResourceBuilder<ProjectResource> dbCreator,
        string? baseAccountId,
        bool seedStagingAccounts,
        string? suggestedBaseAccountId)
    {
        var pending = new PendingInvocation<CreateOptions>();
        dbCreator.WithEnvironment(context =>
        {
            var options = pending.Value ?? new CreateOptions(baseAccountId, seedStagingAccounts);
            context.EnvironmentVariables["stagingSeed__enabled"] = options.SeedStagingAccounts ? "true" : "false";
            if (options.BaseAccountId is not null)
            {
                context.EnvironmentVariables["baseAccount__id"] = options.BaseAccountId;
            }
        });

        dbCreator.WithCommand(
            name: "create-with-options",
            displayName: "Create with options…",
            executeCommand: context =>
            {
                var options = new CreateOptions(
                    Text(context.Arguments, BaseAccountInput),
                    Checked(context.Arguments, StagingSeedInput));
                return StartWithAsync(context, dbCreator.Resource, pending, options);
            },
            commandOptions: new CommandOptions
            {
                Description = "Runs 2sInitDb with the base account and the staging accounts you pick.",
                IconName = "DatabaseArrowRight",
                IconVariant = IconVariant.Regular,
                Arguments =
                [
                    new InteractionInput
                    {
                        Name = BaseAccountInput,
                        Label = "Base account id",
                        Description = "The owner's account id: under Entra ID the lower-case {tid}:{oid}, under AuthKit or Keycloak the sub. Empty: DbCreator's default.",
                        InputType = InputType.Text,
                        Value = baseAccountId ?? suggestedBaseAccountId,
                    },
                    new InteractionInput
                    {
                        Name = StagingSeedInput,
                        Label = "Seed the AuthKit staging accounts",
                        InputType = InputType.Boolean,
                        Value = seedStagingAccounts ? "true" : "false",
                    },
                ],
                UpdateState = IdleOnly,
            });

        return dbCreator;
    }

    private static List<InteractionInput> CreateSeedInputs(string[] sets, string? defaultOwner)
    {
        var inputs = sets
            .Select(set => new InteractionInput
            {
                Name = SetInputPrefix + set,
                Label = $"Data set {set}",
                InputType = InputType.Boolean,
                Value = set == DefaultSet ? "true" : "false",
            })
            .ToList();

        inputs.AddRange(
        [
            TextInput(FolderInput, "Folder (--path)", "A folder with set.json, or a folder of such folders; its sets join the built-in ones. Without a picked set, its sets are seeded."),
            TextInput(OwnerInput, "Owner (--owner)", "The account that owns a created organization and project. Empty: baseAccount:id.", defaultOwner),
            TextInput(OrganizationInput, "Organization (--organization)", "Empty: the Organization of the sets' set.json."),
            TextInput(ProjectInput, "Project (--project)", "Empty: the Project of the sets' set.json."),
            TextInput(ViewInput, "View (--view)", "Empty: the View of the sets' set.json, otherwise default."),
            BooleanInput(DryRunInput, "Dry run: decide every outcome and write nothing"),
            BooleanInput(AgainInput, "Again: seed a set that is seeded already"),
            BooleanInput(ForceInput, "Force: store schemas and configurations that break each other"),
            BooleanInput(PublicPairsOnlyInput, "Public key/value pairs only"),
        ]);

        return inputs;
    }

    private static Task<ExecuteCommandResult> SeedAsync(
        ExecuteCommandContext context,
        IResource dbSeed,
        PendingInvocation<string[]> pending,
        string[] sets)
    {
        var inputs = context.Arguments;
        var arguments = new List<string> { "seed" };
        arguments.AddRange(sets.Where(set => Checked(inputs, SetInputPrefix + set)));

        AddOption(arguments, "--path", Text(inputs, FolderInput));
        AddOption(arguments, "--owner", Text(inputs, OwnerInput));
        AddOption(arguments, "--organization", Text(inputs, OrganizationInput));
        AddOption(arguments, "--project", Text(inputs, ProjectInput));
        AddOption(arguments, "--view", Text(inputs, ViewInput));
        AddFlag(arguments, "--dry-run", Checked(inputs, DryRunInput));
        AddFlag(arguments, "--again", Checked(inputs, AgainInput));
        AddFlag(arguments, "--force", Checked(inputs, ForceInput));
        AddFlag(arguments, "--public-pairs-only", Checked(inputs, PublicPairsOnlyInput));

        context.Logger.LogInformation("Seeding with: {Arguments}", string.Join(' ', arguments));
        return StartWithAsync(context, dbSeed, pending, arguments.ToArray());
    }

    // Keeps the options for the start, starts the resource and waits until its process has started (its arguments
    // and environment are evaluated then), so the plain Start that follows gets the defaults again.
    private static async Task<ExecuteCommandResult> StartWithAsync<T>(
        ExecuteCommandContext context,
        IResource resource,
        PendingInvocation<T> pending,
        T options)
        where T : class
    {
        var commands = context.Services.GetRequiredService<ResourceCommandService>();
        var notifications = context.Services.GetRequiredService<ResourceNotificationService>();
        var cancellationToken = context.CancellationToken;

        if (notifications.TryGetCurrentState(resource.Name, out var current) && IsBusy(current.Snapshot.State?.Text))
        {
            return CommandResults.Failure($"{resource.Name} is running; wait for it to finish or stop it first.");
        }

        if (!pending.TrySet(options))
        {
            return CommandResults.Failure($"{resource.Name} is being started with options already.");
        }

        try
        {
            var requestedAt = DateTime.UtcNow;
            var started = await commands.ExecuteCommandAsync(resource, KnownResourceCommands.StartCommand, cancellationToken);
            if (!started.Success)
            {
                return CommandResults.Failure($"{resource.Name} did not start: {started.Message}");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StartTimeout);
            try
            {
                await notifications.WaitForResourceAsync(
                    resource.Name,
                    e => e.Snapshot.State?.Text is { } state
                        && (state == KnownResourceStates.FailedToStart
                            || (e.Snapshot.StartTimeStamp?.ToUniversalTime() >= requestedAt.AddSeconds(-1)
                                && (state == KnownResourceStates.Running || state == KnownResourceStates.Finished || state == KnownResourceStates.Exited))),
                    timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return CommandResults.Failure($"{resource.Name} did not start within {StartTimeout.TotalMinutes} minutes; see its console log.");
            }

            return CommandResults.Success();
        }
        finally
        {
            pending.Clear();
        }
    }

    private static ResourceCommandState IdleOnly(UpdateCommandStateContext state)
        => IsBusy(state.ResourceSnapshot.State?.Text) ? ResourceCommandState.Disabled : ResourceCommandState.Enabled;

    private static bool IsBusy(string? state)
        => state == KnownResourceStates.Running || state == KnownResourceStates.Starting || state == KnownResourceStates.Waiting;

    private static InteractionInput TextInput(string name, string label, string description, string? value = null)
        => new() { Name = name, Label = label, Description = description, InputType = InputType.Text, Value = value };

    private static InteractionInput BooleanInput(string name, string label)
        => new() { Name = name, Label = label, InputType = InputType.Boolean, Value = "false" };

    private static bool IsChecked(string? value)
        => bool.TryParse(value, out var isChecked) && isChecked;

    private static bool Checked(InteractionInputCollection inputs, string name)
        => inputs.TryGetByName(name, out var input) && IsChecked(input.Value);

    private static string? Text(InteractionInputCollection inputs, string name)
        => inputs.TryGetByName(name, out var input) && !string.IsNullOrWhiteSpace(input.Value) ? input.Value.Trim() : null;

    private static void AddOption(List<string> arguments, string option, string? value)
    {
        if (value is not null)
        {
            arguments.Add(option);
            arguments.Add(value);
        }
    }

    private static void AddFlag(List<string> arguments, string flag, bool isSet)
    {
        if (isSet)
        {
            arguments.Add(flag);
        }
    }

    private sealed record CreateOptions(string? BaseAccountId, bool SeedStagingAccounts);

    private sealed class PendingInvocation<T>
        where T : class
    {
        private T? _value;

        public T? Value => Volatile.Read(ref _value);

        public bool TrySet(T value) => Interlocked.CompareExchange(ref _value, value, null) is null;

        public void Clear() => Volatile.Write(ref _value, null);
    }
}
