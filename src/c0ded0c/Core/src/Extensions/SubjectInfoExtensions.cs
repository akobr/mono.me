namespace c0ded0c.Core
{
    public static class SubjectInfoExtensions
    {
        public static TMutable? GetMutableTag<TMutable>(this ISubjectInfo subjectInfo)
            where TMutable : class
        {
            return subjectInfo.MutableTag as TMutable;
        }
    }
}
