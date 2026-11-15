namespace _42.Platform.Storyteller.Api.Models;

// ErrorResponse.Details of a 401 StepUpRequired: the seconds a sign-in counts (the max_age of the challenge), for clients
// that cannot read WWW-Authenticate.
public sealed record StepUpDetails(long MaxAge);
