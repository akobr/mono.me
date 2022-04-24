namespace _42.Monorepo.Repo.Generator
{
    internal class WorkStats
    {
        public int WorksteadsCount { get; set; }

        public int ProjectsCount { get; set; }

        public int CommitsCount { get; set; }

        public int AddedFilesCount { get; set; }

        public int UpdatedFilesCount { get; set; }

        public int DeletedFilesCount { get; set; }

        public long NumberOfCodeLines { get; set; }

        public void Add(WorkStats other)
        {
            WorksteadsCount += other.WorksteadsCount;
            ProjectsCount += other.ProjectsCount;
            CommitsCount += other.CommitsCount;
            AddedFilesCount += other.AddedFilesCount;
            UpdatedFilesCount += other.UpdatedFilesCount;
            DeletedFilesCount += other.DeletedFilesCount;
            NumberOfCodeLines += other.NumberOfCodeLines;
        }
    }
}
