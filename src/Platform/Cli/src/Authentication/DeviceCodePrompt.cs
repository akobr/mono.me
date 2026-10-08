using System;

namespace _42.Platform.Cli.Authentication;

// What the user needs to finish a device sign-in in a browser.
public sealed record DeviceCodePrompt(string UserCode, Uri VerificationUri, Uri? VerificationUriComplete, TimeSpan ExpiresIn);
