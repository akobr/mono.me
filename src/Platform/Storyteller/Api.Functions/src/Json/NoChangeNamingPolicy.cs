using System.Text.Json;

namespace _42.Platform.Storyteller.Api.Json;

public class NoChangeNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name)
    {
        return name;
    }
}
