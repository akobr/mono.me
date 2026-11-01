using Microsoft.Extensions.DependencyInjection;

namespace _42.Crumble.Playground.Aspire.Host;

public static class ExplicitStartupExtensions
{
    public static IResourceBuilder<T> WithExplicitStart<T>(this IResourceBuilder<T> builder)
        where T : IResource
    {
        builder.ApplicationBuilder.Eventing.Subscribe<BeforeResourceStartedEvent>(builder.Resource, async (evt, ct) =>
        {
            var rns = evt.Services.GetRequiredService<ResourceNotificationService>();

            // This is possibly the last safe place to update the resource's annotations
            // we need to do it this late because the built in lifecycle annotations are added *very* late
            var startCommand = evt.Resource.Annotations.OfType<ResourceCommandAnnotation>().FirstOrDefault(c => c.Name == KnownResourceCommands.StartCommand);

            if (startCommand is null)
            {
                return;
            }

            evt.Resource.Annotations.Remove(startCommand);

            // This will block the resource from starting until the start command is executed
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // Create a new command that clones the start command
            var newCommand = new ResourceCommandAnnotation(
                name: startCommand.Name,
                displayName: startCommand.DisplayName,
                updateState: context => !tcs.Task.IsCompleted ? ResourceCommandState.Enabled : startCommand.UpdateState(context),
                executeCommand: context =>
                {
                    if (tcs.Task.IsCompleted)
                        return startCommand.ExecuteCommand(context);

                    tcs.SetResult();
                    return Task.FromResult(CommandResults.Success());
                },
                displayDescription: startCommand.DisplayDescription,
                arguments: startCommand.Arguments,
                confirmationMessage: startCommand.ConfirmationMessage,
                iconName: startCommand.IconName,
                iconVariant: startCommand.IconVariant,
                isHighlighted: startCommand.IsHighlighted,
                visibility: startCommand.Visibility,
                validateArguments: startCommand.ValidateArguments);

            evt.Resource.Annotations.Add(newCommand);

            await rns.PublishUpdateAsync(evt.Resource, s => s with { State = new ResourceStateSnapshot(KnownResourceStates.Waiting, KnownResourceStateStyles.Info) });

            await tcs.Task.WaitAsync(ct);
        });

        return builder;
    }
}
