namespace c0ded0c.PlantUml.Library
{
    public class InheritanceRelationship : Relationship
    {
        public InheritanceRelationship(TypeNameText baseTypeName, TypeNameText subTypeName) : base(baseTypeName, subTypeName, "<|--", baseTypeName.TypeArguments)
        {
        }
    }
}
