namespace _42.Platform.Storyteller.Accessing;

// Machine access backed by identity provider client credentials (client ID and secret exchanged
// for a JWT). PolicyAwareMachineAccessService routes the ClientCredentials kind here.
public interface IIdentityProviderMachineAccessService : IMachineAccessService
{
}
