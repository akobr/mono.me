using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Monorepo.Cli.Extensions;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.Show
{
    [Command(CommandNames.VERSIONS, Description = "Show all versions of a current location.")]
    public class ShowVersionsCommand : BaseSourceCommand
    {
        public ShowVersionsCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        protected override async Task<int> ExecuteAsync()
        {
            var item = Context.Item;
            var record = item.Record;
            var exactVersions = await item.GetExactVersionsAsync();
            var versionFileFullPath = await item.TryGetVersionFilePathAsync();

            Console.WriteHeader(
                $"{record.GetTypeAsString()}: ",
                record.Name.ThemedHighlight(Console.Theme));

            Console.WriteLine($"Version:               {exactVersions.Version}");

            if (!string.IsNullOrWhiteSpace(versionFileFullPath))
            {
                var versionFileRepoPath = versionFileFullPath.GetRelativePath(Context.Repository.Record.Path);

                var definedVersion = await item.TryGetDefinedVersionAsync();
                Console.WriteLine(
                    $"Defined version:       {definedVersion?.Template ?? "unknown"}",
                    $" ({versionFileRepoPath})".ThemedLowlight(Console.Theme));
            }

            Console.WriteLine($"Package version:       {exactVersions.PackageVersion}");
            Console.WriteLine($"Assembly version:      {exactVersions.AssemblyVersion}");
            Console.WriteLine($"Assembly file version: {exactVersions.AssemblyFileVersion}");
            Console.WriteLine($"Informational version: {exactVersions.AssemblyInformationalVersion}");

            return ExitCodes.SUCCESS;
        }
    }
}
