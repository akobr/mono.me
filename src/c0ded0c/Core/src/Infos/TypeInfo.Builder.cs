using System.Collections.Immutable;

namespace c0ded0c.Core
{
    public partial class TypeInfo
    {
        public sealed class Builder : BaseBuilder
        {
            internal Builder(TypeInfo type)
                : base(type)
            {
                Members = type.members.ToBuilder();
                NestedTypes = type.nestedTypes.ToBuilder();
            }

            public ImmutableDictionary<string, IMemberInfo>.Builder Members { get; private set; }

            public ImmutableDictionary<string, ITypeInfo>.Builder NestedTypes { get; private set; }

            public void AdMember(IMemberInfo member)
            {
                Members.Add(member.Key.FullName, member);
            }

            public void AddNestedType(ITypeInfo nestedType)
            {
                NestedTypes.Add(nestedType.Name, nestedType);
            }

            public TypeInfo ToImmutable()
            {
                return new TypeInfo(this);
            }
        }
    }
}
