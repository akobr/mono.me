using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace c0ded0c.Core
{
    public sealed partial class TypeInfo : SubjectInfo, ITypeInfo
    {
        private ImmutableDictionary<string, IMemberInfo> members;
        private ImmutableDictionary<string, ITypeInfo> nestedTypes;

        public TypeInfo(IIdentificator key)
            : base(key)
        {
            members = ImmutableDictionary<string, IMemberInfo>.Empty.WithComparers(StringComparer.Ordinal);
            nestedTypes = ImmutableDictionary<string, ITypeInfo>.Empty.WithComparers(StringComparer.Ordinal);
        }

        public TypeInfo(ITypeInfo toClone)
            : base(toClone)
        {
            members = ImmutableDictionary.CreateRange(StringComparer.Ordinal, toClone.Members);
            nestedTypes = ImmutableDictionary.CreateRange(StringComparer.Ordinal, toClone.NestedTypes);
        }

        private TypeInfo(TypeInfo toClone)
            : base(toClone)
        {
            members = toClone.members;
            nestedTypes = toClone.nestedTypes;
        }

        private TypeInfo(Builder builder)
            : base(builder)
        {
            members = builder.Members.ToImmutable();
            nestedTypes = builder.NestedTypes.ToImmutable();
        }

        [Artifact]
        public IImmutableDictionary<string, IMemberInfo> Members => members;

        [Artifact]
        public IImmutableDictionary<string, ITypeInfo> NestedTypes => nestedTypes;

        public override IEnumerable<ISubjectInfo> GetChildren()
        {
            return Enumerable.Concat<ISubjectInfo>(nestedTypes.Values, members.Values);
        }

        public TypeInfo SetMember(IMemberInfo member)
        {
            return new TypeInfo(this)
            {
                members = members.Remove(member.Key.FullName).SetItem(member.Key.FullName, member),
            };
        }

        public TypeInfo RemoveMember(string fullName)
        {
            return new TypeInfo(this)
            {
                members = members.Remove(fullName),
            };
        }

        public TypeInfo SetNastedType(ITypeInfo typeInfo)
        {
            return new TypeInfo(this)
            {
                nestedTypes = nestedTypes.Remove(typeInfo.Name).SetItem(typeInfo.Name, typeInfo),
            };
        }

        public TypeInfo RemoveNastedType(string name)
        {
            return new TypeInfo(this)
            {
                nestedTypes = nestedTypes.Remove(name),
            };
        }

        public Builder ToBuilder()
        {
            return new Builder(this);
        }

        public static TypeInfo From(ITypeInfo type)
        {
            return type is TypeInfo familiar
                ? familiar
                : new TypeInfo(type);
        }
    }
}
