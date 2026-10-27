namespace _42.Platform.Storyteller.ConfigurationFormats;

// The flat representations of a configuration. ConfigurationFormatNames has their names and media types.
public enum ConfigurationFormat
{
    // A JSON object of IConfiguration keys and string values: db:hosts:0.
    Flat = 1,

    // Spring Boot .properties: db.hosts[0]=a.
    Properties,

    // A .env file: DB__HOSTS__0=a.
    Env,

    // YAML 1.2, the nested document without nulls and empty containers.
    Yaml,
}
