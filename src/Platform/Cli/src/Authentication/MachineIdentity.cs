using System;
using System.Collections.Generic;

namespace _42.Platform.Cli.Authentication;

// What sform knows about the machine it signs in as. Never holds a secret.
// Variables are the names of the credential variables in use, for messages.
public sealed record MachineIdentity(
    MachineSignInKind Kind,
    IReadOnlyList<string> Variables,
    string? MachineId,
    string? Organization,
    string? Project,
    DateTimeOffset? TokenExpiresAt = null);
