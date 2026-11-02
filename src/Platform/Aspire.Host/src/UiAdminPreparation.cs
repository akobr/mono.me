using Aspire.Hosting.JavaScript;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Aspire.Host;

/// <summary>
/// Prepares the admin UI before its Vite dev server starts: the <c>ui-admin-sdk</c> executable installs and builds the
/// TypeScript SDK, then the built-in <c>ui-admin-installer</c> installs the UI's packages, both through their
/// <c>prepare:local</c> scripts, which skip what is up to date. Adds the <c>Clean reinstall</c> command.
/// </summary>
internal static class UiAdminPreparation
{
    private const string SdkResourceName = "ui-admin-sdk";

    private static readonly TimeSpan StepTimeout = TimeSpan.FromMinutes(15);

    private static readonly string[] Stamps =
    [
        "Storyteller/sdk.typescript/src/node_modules/.prepare-local-install",
        "Storyteller/sdk.typescript/src/dist/.prepare-local-build",
        "ui.admin/src/node_modules/.prepare-local-install",
    ];

    public static IResourceBuilder<ViteAppResource> WithLocalPreparation(this IResourceBuilder<ViteAppResource> uiAdmin, string platformDirectory)
    {
        var builder = uiAdmin.ApplicationBuilder;
        if (!builder.ExecutionContext.IsRunMode)
        {
            return uiAdmin;
        }

        var sdk = builder
            .AddExecutable(SdkResourceName, "node", Path.Combine(platformDirectory, "Storyteller/sdk.typescript/src"), "scripts/prepare-local.mjs")
            .WithParentRelationship(uiAdmin)
            .ExcludeFromManifest();

        // AddViteApp has already created ui-admin-installer, and ui-admin waits for it to complete. A second WithNpm
        // keeps that installer and only replaces its arguments, which it reads from the last annotation at start.
        uiAdmin.WithNpm(install: true, installCommand: "run", installArgs: ["prepare:local"]);
        var installer = uiAdmin.Resource.Annotations.OfType<JavaScriptPackageInstallerAnnotation>().Last().Resource;
        builder.CreateResourceBuilder(installer).WaitForCompletion(sdk);

        uiAdmin.WithCommand(
            name: "clean-reinstall",
            displayName: "Clean reinstall",
            executeCommand: context => CleanReinstallAsync(context, uiAdmin.Resource, sdk.Resource, installer, platformDirectory),
            commandOptions: new CommandOptions
            {
                Description = "Stops the admin UI, reinstalls and rebuilds the TypeScript SDK and the UI packages from scratch, and starts it again.",
                ConfirmationMessage = "Stop the admin UI and reinstall the SDK and the UI packages from scratch?",
                IconName = "ArrowSync",
                IconVariant = IconVariant.Regular,
                UpdateState = state => state.ResourceSnapshot.State?.Text == KnownResourceStates.Starting
                    ? ResourceCommandState.Disabled
                    : ResourceCommandState.Enabled,
            });

        return uiAdmin;
    }

    private static async Task<ExecuteCommandResult> CleanReinstallAsync(
        ExecuteCommandContext context,
        IResource uiAdmin,
        IResource sdk,
        IResource installer,
        string platformDirectory)
    {
        var commands = context.ServiceProvider.GetRequiredService<ResourceCommandService>();
        var notifications = context.ServiceProvider.GetRequiredService<ResourceNotificationService>();
        var cancellationToken = context.CancellationToken;

        // npm ci replaces node_modules, which the running dev server holds open. A dev server that is not running (it
        // failed, or waits for a failed step) has nothing to stop, so a failed stop matters only while it runs.
        var wasRunning = notifications.TryGetCurrentState(uiAdmin.Name, out var current)
            && current.Snapshot.State?.Text == KnownResourceStates.Running;
        var stopped = await commands.ExecuteCommandAsync(uiAdmin, KnownResourceCommands.StopCommand, cancellationToken);
        if (!stopped.Success && wasRunning)
        {
            return CommandResults.Failure($"ui-admin did not stop: {stopped.ErrorMessage}");
        }

        // Without stamps, both prepare:local scripts run every step. A stamp's folder (node_modules, dist) may be gone.
        foreach (var stamp in Stamps)
        {
            var path = Path.Combine(platformDirectory, stamp);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        foreach (var step in new[] { sdk, installer })
        {
            var error = await RunToCompletionAsync(commands, notifications, step, cancellationToken);
            if (error is not null)
            {
                return CommandResults.Failure(error);
            }
        }

        var started = await commands.ExecuteCommandAsync(uiAdmin, KnownResourceCommands.StartCommand, cancellationToken);
        return started.Success ? CommandResults.Success() : CommandResults.Failure($"ui-admin did not start: {started.ErrorMessage}");
    }

    private static async Task<string?> RunToCompletionAsync(
        ResourceCommandService commands,
        ResourceNotificationService notifications,
        IResource step,
        CancellationToken cancellationToken)
    {
        var requestedAt = DateTime.UtcNow;
        var started = await commands.ExecuteCommandAsync(step, KnownResourceCommands.StartCommand, cancellationToken);
        if (!started.Success)
        {
            return $"{step.Name} did not start: {started.ErrorMessage}";
        }

        // The start time tells this run apart from the finished previous one; a failed start may have no start time.
        // A full npm ci and build take minutes, so a step still running after the timeout is reported as hanging.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StepTimeout);
        ResourceEvent finished;
        try
        {
            finished = await notifications.WaitForResourceAsync(
                step.Name,
                e => e.Snapshot.State?.Text is { } state
                    && (state == KnownResourceStates.FailedToStart
                        || ((state == KnownResourceStates.Finished || state == KnownResourceStates.Exited)
                            && e.Snapshot.StartTimeStamp?.ToUniversalTime() >= requestedAt.AddSeconds(-1))),
                timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"{step.Name} did not finish within {StepTimeout.TotalMinutes} minutes; see its console log.";
        }

        return finished.Snapshot.ExitCode is 0
            ? null
            : $"{step.Name} failed (exit code {finished.Snapshot.ExitCode?.ToString() ?? "none"}); see its console log.";
    }
}
