namespace _42.Platform.Storyteller;

public class CosmosDbOptions
{
    public const string SectionName = "CosmosDb";

    public string Connection { get; set; } = string.Empty;

    public bool? ShouldAcceptAnyCertificate { get; set; }
}
