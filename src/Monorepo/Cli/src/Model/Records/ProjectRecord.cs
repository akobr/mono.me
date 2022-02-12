namespace _42.Monorepo.Cli.Model.Records
{
    public class ProjectRecord : Record, IProjectRecord
    {
        public ProjectRecord(string path, IRecord parent)
            : base(path, parent)
        {
            // no operation
        }

        public override RecordType Type => RecordType.Project;
    }
}
