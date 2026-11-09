using System;

namespace _42.Platform.Cli.Configuration;

// The SFORM_* variables that change how sform behaves, read once per process; SFORM_HOME itself is resolved by CliPaths.
// Reading never throws: a value sform does not honour is ignored, and `sform account` says so (TokenStoreNotice).
public sealed class SformEnvironment
{
    private SformEnvironment()
    {
    }

    // SFORM_TOKEN_STORE=file together with SFORM_HOME: the AuthKit session and the MSAL cache are plain files in SFORM_HOME.
    public bool IsFileTokenStore { get; private init; }

    // What `sform account` warns about the token store: the unencrypted files in use, or a value that was ignored.
    public string? TokenStoreNotice { get; private init; }

    // SFORM_DEVICE_CODE_OUTPUT=json, in any case: the device code prompt is also one JSON line on standard error.
    public bool IsDeviceCodeJson { get; private init; }

    // The value of SFORM_BROWSER (a program), or of BROWSER (a list of commands) when SFORM_BROWSER is not set; null opens
    // the default browser. BrowserLauncher reads it by BrowserVariable.
    public string? BrowserCommand { get; private init; }

    // The variable the value came from: SFORM_BROWSER or BROWSER.
    public string? BrowserVariable { get; private init; }

    public static SformEnvironment FromEnvironment(Func<string, string?> getVariable, CliPaths paths)
    {
        var tokenStore = Read(getVariable, SformVariables.TOKEN_STORE);
        var isFileRequested = string.Equals(tokenStore, SformVariables.TOKEN_STORE_FILE, StringComparison.OrdinalIgnoreCase);
        var isFileTokenStore = isFileRequested && paths.IsHomeSet;
        var browser = Read(getVariable, SformVariables.BROWSER);
        var browserVariable = SformVariables.BROWSER;

        if (browser is null)
        {
            browser = Read(getVariable, SformVariables.CONVENTIONAL_BROWSER);
            browserVariable = SformVariables.CONVENTIONAL_BROWSER;
        }

        return new SformEnvironment
        {
            IsFileTokenStore = isFileTokenStore,
            TokenStoreNotice = (tokenStore, isFileRequested, isFileTokenStore) switch
            {
                (null, _, _) => null,
                (_, _, true) => $"The session is kept unencrypted in {paths.Home} ({SformVariables.TOKEN_STORE}={SformVariables.TOKEN_STORE_FILE}); keep the folder private.",
                (_, true, false) => $"{SformVariables.TOKEN_STORE}={tokenStore} is ignored without {SformVariables.HOME}; the session is kept in the protected store.",
                _ => $"{SformVariables.TOKEN_STORE}={tokenStore} is ignored: the only value is {SformVariables.TOKEN_STORE_FILE}, together with {SformVariables.HOME}.",
            },
            IsDeviceCodeJson = string.Equals(
                Read(getVariable, SformVariables.DEVICE_CODE_OUTPUT),
                SformVariables.DEVICE_CODE_OUTPUT_JSON,
                StringComparison.OrdinalIgnoreCase),
            BrowserCommand = browser,
            BrowserVariable = browser is null ? null : browserVariable,
        };
    }

    // Trimmed; null when unset or blank.
    private static string? Read(Func<string, string?> getVariable, string name)
    {
        var value = getVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
