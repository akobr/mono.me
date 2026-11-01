namespace _42.Crumble.Playground.Aspire.Host;

public static class ResourceBuilderExtensions
{
    public static IResourceBuilder<ProjectResource> WithOpenOrleansDashboardCommand(this IResourceBuilder<ProjectResource> builder)
    {
        builder.WithCommand(
            name: "openDashboard",
            displayName: "Open Dashboard",
            executeCommand: context => OnOpenTabCommandAsync(builder, context),
            commandOptions: new CommandOptions
            {
                UpdateState = _ => ResourceCommandState.Enabled,
                IconName = "Board",
                IconVariant = IconVariant.Regular,
            });

        return builder;
    }

    private static async Task<ExecuteCommandResult> OnOpenTabCommandAsync(IResourceBuilder<ProjectResource> builder, ExecuteCommandContext context)
    {
        var endpoint = builder.Resource.GetEndpoints().First();
        var url = $"{endpoint.Url}/dashboard";

        return new ExecuteCommandResult
        {
            Success = false,
            Message = url,
        };
    }
}
