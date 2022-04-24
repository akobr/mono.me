using System.Text;
using Bogus;
using Humanizer;
using Microsoft.CSharp;

namespace _42.Monorepo.Repo.Generator
{
    internal static class RoslynExtensions
    {
        private static readonly CSharpCodeProvider CodeProvider = new();

        public static string GetValidName(this Randomizer @this)
        {
            var name = @this.Word().Dehumanize().RemoveInvalidCharacters();
            return CodeProvider.CreateValidIdentifier(name);
        }

        private static string RemoveInvalidCharacters(this string name)
        {
            var builder = new StringBuilder(name.Length);

            foreach (var character in name)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }
    }
}
