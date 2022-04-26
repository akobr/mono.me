namespace c0ded0c.Core
{
    public class SubjectStoreModel : IStoreModel
    {
        private readonly ISubjectInfo subject;

        public SubjectStoreModel(ISubjectInfo subject)
        {
            this.subject = subject;
        }

        public string Hash => subject.Key.Hash;

        public string Path => subject.Key.Path;

        public string FullName => subject.Key.FullName;

        public string Name => subject.Key.Name;

        public IExpansion? Expansion => subject.Expansion;
    }
}
