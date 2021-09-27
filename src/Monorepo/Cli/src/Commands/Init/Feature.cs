namespace _42.Monorepo.Cli.Commands.Init
{
    public class Feature
    {
        public Feature(string id, string name, string description)
        {
            Id = id;
            Name = name;
            Description = description;
        }

        public string Id { get; }

        public string Name { get; }

        public string Description { get; }
    }
}
