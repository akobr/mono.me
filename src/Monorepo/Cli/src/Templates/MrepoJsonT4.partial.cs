namespace _42.Monorepo.Cli.Templates
{
    public partial class MrepoJsonT4
    {
        public MrepoJsonT4(MrepoJsonModel model)
        {
            Model = model;
        }

        private MrepoJsonModel Model { get; }
    }

    public class MrepoJsonModel
    {
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Features { get; set; } = string.Empty;
    }
}
