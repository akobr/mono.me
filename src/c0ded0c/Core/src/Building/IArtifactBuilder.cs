namespace c0ded0c.Core
{
    public interface IArtifactBuilder
    {
        IArtifactInfo Build<TObject>(TObject value, string key, ISubjectInfo target)
            where TObject : class;

        IArtifactInfo BuildSpecific(IContent value, string key, ISubjectInfo target);
    }
}
