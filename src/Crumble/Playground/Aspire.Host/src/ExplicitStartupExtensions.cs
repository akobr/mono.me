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
            var startCommand = evt.Resource.Annotations.OfType<ResourceCommandAnnotation>().FirstOrDefault(c => c.Name == "resource-start");

            if (startCommand is null)
            {
                return;
            }

            evt.Resource.Annotations.Remove(startCommand);

            // This will block the resource from starting until the "resource-start" command is executed
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // Create a new command that clones the start command
            var newCommand = new ResourceCommandAnnotation(
                startCommand.Name,
                startCommand.DisplayName,
                context => !tcs.Task.IsCompleted ? ResourceCommandState.Enabled : startCommand.UpdateState(context),
                context =>
                {
                    if (tcs.Task.IsCompleted)
                        return startCommand.ExecuteCommand(context);

                    tcs.SetResult();
                    return Task.FromResult(CommandResults.Success());
                },
                startCommand.DisplayDescription,
                startCommand.Parameter,
                startCommand.ConfirmationMessage,
                startCommand.IconName,
                startCommand.IconVariant,
                startCommand.IsHighlighted);

            evt.Resource.Annotations.Add(newCommand);

            await rns.PublishUpdateAsync(evt.Resource, s => s with { State = new ResourceStateSnapshot(KnownResourceStates.Waiting, KnownResourceStateStyles.Info) });

            await tcs.Task.WaitAsync(ct);
        });

        return builder;
    }
}
