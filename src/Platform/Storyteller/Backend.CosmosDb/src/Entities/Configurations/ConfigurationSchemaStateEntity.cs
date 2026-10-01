namespace _42.Platform.Storyteller.Entities.Configurations;

public record class ConfigurationSchemaStateEntity : Entity
{
    public ulong LastVersion { get; init; }
}
