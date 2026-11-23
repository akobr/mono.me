using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Notifying;
using _42.Platform.Storyteller.Transferring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Notifying.ViewInboxTestData;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring.TransferTestData;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Notifying;

// The inbox and the jobs in one container, as the API host registers them: the jobs push into the inbox, and a handler
// of the inbox starts jobs.
public class ViewInboxRegistrationTests
{
    private static readonly ServiceProviderOptions Validated = new() { ValidateOnBuild = true, ValidateScopes = true };

    private readonly IConfiguration _configuration = new ConfigurationBuilder().Build();
    private readonly ManualTimeProvider _time = new(Start);
    private readonly InMemoryTransferStore _jobs = new();
    private readonly RecordingTransferQueue _queue = new();
    private readonly InMemoryViewInboxStore _items = new();
    private readonly CapturingLogger<ViewTransferService> _logger = new();

    [Fact]
    public async Task AddViewInbox_NextToTheTransfers_ResolvesBothWithAHandlerThatStartsJobs()
    {
        var services = HostServices()
            .AddViewTransfers(_configuration)
            .AddViewInbox(_configuration)
            .AddSingleton<IViewInboxActionHandler, ExportingHandler>();

        await using var provider = services.BuildServiceProvider(Validated);
        var transfers = provider.GetRequiredService<IViewTransferService>().ShouldBeOfType<ViewTransferService>();
        var inbox = provider.GetRequiredService<IViewInboxService>().ShouldBeOfType<ViewInboxService>();
        var publisher = provider.GetRequiredService<IViewInboxPublisher>().ShouldBeOfType<ViewInboxPublisher>();

        // The whole circle of the issue: accepting an action starts an export through the service of the jobs, and the
        // end of that export comes back into the inbox through the publisher.
        var action = await publisher.PushAsync(Push(ViewInboxItemKind.Action, AccountRole.Contributor), TestContext.Current.CancellationToken);
        var accepted = await inbox.AcceptAsync("acme", "main", "default", action.Id, AccountRole.Administrator, Actor, TestContext.Current.CancellationToken);

        var message = _queue.Messages.ShouldHaveSingleItem();
        accepted.State.ShouldBe(ViewInboxItemState.Accepted);
        accepted.Job!.Kind.ShouldBe(ViewInboxJobKind.ViewExport);
        accepted.Job.Id.ShouldBe(message.Id);

        await transfers.ProcessAsync(message, TestContext.Current.CancellationToken);

        var open = await inbox.GetItemsAsync("acme", "main", "default", new ViewInboxQuery { Role = AccountRole.Administrator }, TestContext.Current.CancellationToken);
        var report = open.Items.ShouldHaveSingleItem();
        report.Topic.ShouldBe(ViewInboxTopics.ViewExport);
        report.Title.ShouldBe("Export finished");
        report.CreatedBy.ShouldBe(Actor);
        report.Job.ShouldBe(accepted.Job);
    }

    [Fact]
    public async Task WithoutAddViewInbox_TheTransfersResolveAndPushNothing()
    {
        // the store of the inbox is there all the same, as the registration of the storage adds it for every host
        var services = HostServices().AddViewTransfers(_configuration);

        await using var provider = services.BuildServiceProvider(Validated);
        provider.GetService<IViewInboxPublisher>().ShouldBeNull();
        provider.GetService<IViewInboxService>().ShouldBeNull();
        var transfers = provider.GetRequiredService<IViewTransferService>().ShouldBeOfType<ViewTransferService>();

        var export = await transfers.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Folder, null, Actor, TestContext.Current.CancellationToken);
        var message = _queue.Messages.ShouldHaveSingleItem();

        // the end, and the message that comes again for the job that ended
        await transfers.ProcessAsync(message, TestContext.Current.CancellationToken);
        await transfers.ProcessAsync(message, TestContext.Current.CancellationToken);

        (await transfers.GetExportAsync("acme", "main", "default", export.Id, TestContext.Current.CancellationToken))!.Status.ShouldBe(ViewTransferStatus.Succeeded);
        _items.Writes.ShouldBeEmpty();

        // a push that failed would be a warning
        _logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task TwoHandlersOfOneAction_StopTheInboxServiceAndNotTheTransfers()
    {
        var services = HostServices()
            .AddViewTransfers(_configuration)
            .AddViewInbox(_configuration)
            .AddSingleton<IViewInboxActionHandler, ExportingHandler>()
            .AddSingleton<IViewInboxActionHandler>(new RecordingActionHandler(HandledAction));

        await using var provider = services.BuildServiceProvider(Validated);

        Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<IViewInboxService>()).Message.ShouldContain(HandledAction);

        // no producer depends on the service of the routes: the jobs run and report their ends
        var transfers = provider.GetRequiredService<IViewTransferService>();
        await transfers.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Folder, null, Actor, TestContext.Current.CancellationToken);
        await transfers.ProcessAsync(_queue.Messages.ShouldHaveSingleItem(), TestContext.Current.CancellationToken);

        _items.Writes.ShouldHaveSingleItem().Record.Item.Title.ShouldBe("Export finished");
    }

    [Fact]
    public async Task ARetentionOutsideItsRange_FailsThePushThatNeedsIt_AndNotTheTransfers()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ViewInbox:RetentionDays"] = "0" })
            .Build();
        var services = HostServices()
            .AddViewTransfers(configuration)
            .AddViewInbox(configuration);

        await using var provider = services.BuildServiceProvider(Validated);

        // a host stops at its start, where the wrong setting is seen
        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain("ViewInbox:RetentionDays must be between 1 and 365.");

        // the setting is read by the push, not by the construction of the publisher that the jobs take
        var transfers = provider.GetRequiredService<IViewTransferService>();
        await transfers.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Folder, null, Actor, TestContext.Current.CancellationToken);
        await transfers.ProcessAsync(_queue.Messages.ShouldHaveSingleItem(), TestContext.Current.CancellationToken);

        // a result names its own expiry and needs no retention
        _items.Writes.ShouldHaveSingleItem().Record.Item.Title.ShouldBe("Export finished");

        var publisher = provider.GetRequiredService<IViewInboxPublisher>();
        var exception = await Should.ThrowAsync<OptionsValidationException>(() => publisher.PushAsync(Push(), TestContext.Current.CancellationToken));
        exception.Message.ShouldContain("ViewInbox:RetentionDays must be between 1 and 365.");
        _items.Writes.Count.ShouldBe(1);
    }

    // What a host registers besides the two entry points, as fakes: the stores, the queue, the archives, the services
    // of the four kinds of documents, the clock and logging.
    private IServiceCollection HostServices()
    {
        return new ServiceCollection()
            .AddLogging()
            .AddSingleton<ILogger<ViewTransferService>>(_logger)
            .AddSingleton<TimeProvider>(_time)
            .AddSingleton<IViewTransferStore>(_jobs)
            .AddSingleton<IViewArchiveStorage>(new InMemoryArchiveStorage(_time))
            .AddSingleton<IViewTransferQueue>(_queue)
            .AddSingleton<IAnnotationService>(new FakeAnnotationService())
            .AddSingleton<IConfigurationSchemaService>(new FakeSchemaService())
            .AddSingleton<IConfigurationTemplateService>(new FakeTemplateService())
            .AddSingleton<IConfigurationService>(new FakeConfigurationService())
            .AddSingleton<IViewInboxStore>(_items);
    }

    // What the first real handler does: it starts a job through the service of the jobs, which later reports its end.
    private sealed class ExportingHandler(IViewTransferService transfers) : IViewInboxActionHandler
    {
        public string Action => HandledAction;

        public async Task<ViewInboxActionResult> RunAsync(ViewInboxActionContext context, CancellationToken cancellationToken)
        {
            var export = await transfers.StartExportAsync(
                context.Organization,
                context.Project,
                context.View,
                ViewArchiveFormat.Folder,
                null,
                context.Actor,
                cancellationToken);

            return new ViewInboxActionResult { Job = new ViewInboxJob { Kind = ViewInboxJobKind.ViewExport, Id = export.Id, FileName = export.FileName } };
        }
    }
}
