namespace c0ded0c.Core
{
    public abstract partial class SubjectInfo
    {
        public abstract class BaseBuilder
        {
            protected BaseBuilder(SubjectInfo subject)
            {
                Key = subject.Key;

                if (subject.Expansion != null)
                {
                    Expansion = Core.Expansion.From(subject.Expansion).ToBuilder();
                }

                MutableTag = subject.MutableTag;
            }

            public IIdentificator Key { get; }

            public Expansion.Builder? Expansion { get; set; }

            public object? MutableTag { get; set; }

            public TMutable? GetMutableTag<TMutable>()
                where TMutable : class
            {
                return MutableTag as TMutable;
            }
        }
    }
}
