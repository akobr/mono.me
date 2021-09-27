using Sharprompt;

namespace _42.Monorepo.Cli.Output
{
    public static class PrompterExtensions
    {
        public static bool Confirm(this IPrompter prompter, string message, bool? defaultValue = null)
        {
            return prompter.Confirm(new ConfirmOptions()
            {
                Message = message,
                DefaultValue = defaultValue,
            });
        }
    }
}
