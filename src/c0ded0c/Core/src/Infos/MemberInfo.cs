using System.Collections.Generic;
using System.Linq;

namespace c0ded0c.Core
{
    public sealed partial class MemberInfo : SubjectInfo, IMemberInfo
    {
        public MemberInfo(IIdentificator key)
            : base(key)
        {
            // no operation
        }

        public override IEnumerable<ISubjectInfo> GetChildren()
        {
            return Enumerable.Empty<ISubjectInfo>();
        }
    }
}
