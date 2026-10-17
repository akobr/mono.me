namespace _42.Platform.Storyteller;

// The federation rule a verified platform token matched. RuleNumber counts from 1 within the machine's rules.
public sealed record SubjectTokenMatch(
    FederatedIssuer Issuer,
    FederationRule Rule,
    int RuleNumber,
    string Subject);
