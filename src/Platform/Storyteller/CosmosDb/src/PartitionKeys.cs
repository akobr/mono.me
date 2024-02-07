using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

public static class PartitionKeys
{
    public static string GetResponsibility(string projectName, string responsibilityName)
    {
        return $"{projectName}.{responsibilityName}";
    }

    public static string GetSubject(string projectName, string subjectName)
    {
        return $"{projectName}.{subjectName}";
    }

    public static PartitionKey GetCosmosResponsibility(string projectName, string responsibilityName)
    {
        return new PartitionKey(GetResponsibility(projectName, responsibilityName));
    }

    public static PartitionKey GetCosmosSubject(string projectName, string subjectName)
    {
        return new PartitionKey(GetSubject(projectName, subjectName));
    }
}
