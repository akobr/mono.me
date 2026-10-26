namespace _42.Platform.Storyteller.Accessing;

// An owner of an issuer whose users the bearer pipeline accepts: a user result (IsMachine false) with a Delegation, from
// a user token that names the actor acting for the person. The results of every other owner must be machines. Only the
// validator of Storyteller tokens implements it, because only Storyteller issues user tokens for an actor.
public interface IUserTokenIssuerValidator : IMachineTokenValidator
{
}
