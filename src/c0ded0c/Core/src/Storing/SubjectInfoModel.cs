using System.Collections.Generic;

namespace c0ded0c.Core
{
    public class SubjectInfoModel : SubjectStoreModel
    {
        public SubjectInfoModel(
            ISubjectInfo subject,
            IReadOnlyDictionary<string, object> properties)
            : base(subject)
        {
            Properties = properties;
        }

        public IReadOnlyDictionary<string, object> Properties { get; }
    }
}
