namespace _42.Monorepo.Cli.Templates
{
    public partial class ProjectCsprojT4
    {
        public ProjectCsprojT4(ProjectCsprojModel model)
        {
            Model = model;
        }

        private ProjectCsprojModel Model { get; }
    }

    public class ProjectCsprojModel
    {
        public string AssemblyName { get; set; } = string.Empty;

        public string RootNamespace { get; set; } = string.Empty;

        public bool HasCustomName { get; set; }

        public int DotNetVersion { get; set; }
    }
}
