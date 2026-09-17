namespace _42.Platform.Storyteller.Accessing;

public interface IMachineAuthenticationPolicyStore
{
    Task<MachineAuthenticationPolicy?> GetAsync(string organization, string project);

    Task SetAsync(string organization, string project, MachineAuthenticationPolicy policy);
}
