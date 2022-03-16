namespace _42.Monorepo.Cli.Configuration;

public interface IItemFullOptionsProvider
{
    IItemFullOption GetOptions(string path, string defaultTypeKey = TypeKeys.UNKNOWN);
}
