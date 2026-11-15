namespace _42.Platform.Storyteller.Accessing;

public interface IMachineAuthenticationPolicyStore
{
    Task<MachineAuthenticationPolicy?> GetAsync(string organization, string project);

    // A changed policy writes the caller's access audit record with it.
    Task SetAsync(string organization, string project, MachineAuthenticationPolicy policy, AccessCaller caller);
}
