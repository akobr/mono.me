namespace _42.Monorepo.Cli.Model.Records
{
    public class ProjectRecord : ItemRecord, IProjectRecord
    {
        public ProjectRecord(string path, IItemRecord parent)
            : base(path, parent)
        {
            // no operation
        }

        public override ItemType Type => ItemType.Project;
    }
}
