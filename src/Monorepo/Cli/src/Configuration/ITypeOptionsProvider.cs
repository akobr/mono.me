namespace _42.Monorepo.Cli.Configuration
{
    public interface ITypeOptionsProvider
    {
        TypeOptions GetOptions(string typeName);
    }
}
