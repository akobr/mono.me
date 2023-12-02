using System.Collections.Generic;
using Sharprompt;

namespace _42.Monorepo.Cli.Output
{
    public interface IPrompter
    {
        T Input<T>(InputOptions<T> options);

        bool Confirm(ConfirmOptions options);

        string Password(PasswordOptions options);

        IEnumerable<T> List<T>(ListOptions<T> options);

        T Select<T>(SelectOptions<T> options);

        IEnumerable<T> MultiSelect<T>(MultiSelectOptions<T> options);
    }
}
