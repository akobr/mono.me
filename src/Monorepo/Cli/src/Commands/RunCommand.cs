using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Monorepo.Cli.Scripting;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands
{
    [Command(CommandNames.RUN, "x", Description = "Run any preconfigured command.", UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect)]
    public class RunCommand : BaseCommand
    {
        private readonly IScriptingService _scripting;
        private readonly CommandLineApplication _application;

        public RunCommand(IExtendedConsole console, ICommandContext context, IScriptingService scripting, CommandLineApplication application)
            : base(console, context)
        {
            _scripting = scripting;
            _application = application;
        }

        [Argument(0, "script-name", "Name of a script to execute.")]
        public string ScriptName { get; set; } = string.Empty;

        protected override Task<int> ExecuteAsync()
        {
            if (string.IsNullOrWhiteSpace(ScriptName))
            {
                Console.WriteImportant("A script name needs to be specified.");
                WriteAvailableScripts();
                return Task.FromResult(ExitCodes.ERROR_WRONG_INPUT);
            }

            var scriptContext = new ScriptContext(ScriptName, Context.Item) { Args = _application.RemainingArguments };

            if (!_scripting.HasScript(scriptContext))
            {
                Console.WriteImportant($"Unrecognized script '{scriptContext.ScriptName}'.");
                WriteAvailableScripts();
                return Task.FromResult(ExitCodes.ERROR_WRONG_INPUT);
            }

            return _scripting.ExecuteScriptAsync(scriptContext);
        }

        private void WriteAvailableScripts()
        {
            Console.WriteLine();
            Console.WriteLine("Available scripts for current position are");

            foreach (var script in _scripting.GetAvailableScriptNames(Context.Item).OrderBy(s => s))
            {
                Console.WriteLine("  > ", script.ThemedHighlight(Console.Theme));
            }
        }
    }
}
