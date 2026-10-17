namespace _42.Platform.Storyteller.Accessing.Federation;

// Machine access without a secret: the workload exchanges a token of a trusted OIDC issuer for a short-lived
// Storyteller token. PolicyAwareMachineAccessService routes the Federated kind here.
public interface IFederatedMachineAccessService : IMachineAccessService
{
}
