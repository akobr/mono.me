namespace _42.Monorepo.Cli.Templates
{
    public partial class VersionJsonT4
    {
        public VersionJsonT4(VersionJsonModel model)
        {
            Model = model;
        }

        private VersionJsonModel Model { get; }
    }

    public class VersionJsonModel
    {
        public string Version { get; set; } = string.Empty;

        public bool IsHierarchical { get; set; }
    }
}
