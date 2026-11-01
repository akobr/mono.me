using _42.Testing.UI.Automation.Runtime.Access;
using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Reporting;

namespace _42.Testing.UI.Automation.Runtime.Settings;

// The checks of a configured suite. Messages name the setting and, for a secret, where to set it; never a secret value.
internal static class SettingsValidator
{
    private const int MaxRetries = 10;

    public static List<string> Validate(UiAutomationSettings settings, IUiAutomationSetup? setup)
    {
        var errors = new List<string>();

        ValidateTarget(settings.Target, setup, errors);
        ValidateBrowser(settings.Browser, errors);
        ValidateExecution(settings.Execution, errors);
        ValidateUsers(settings, setup, errors);

        if (settings.Browser.Provider == BrowserProviderKind.BrowserStack)
        {
            ValidateBrowserStack(settings.BrowserStack, errors);
        }

        if (settings.Reporting.Enabled || settings.IsReplay)
        {
            errors.AddRange(ValidateReporting(settings.Reporting));
        }

        setup?.Validate(settings, errors);
        return errors;
    }

    public static List<string> ValidateReporting(ReportingSettings reporting)
    {
        var errors = new List<string>();

        if (!reporting.Enabled && !string.IsNullOrWhiteSpace(reporting.ReplayFile))
        {
            errors.Add("Reporting:ReplayFile needs Reporting:Enabled=true.");
        }

        RequireAtLeast(reporting.BatchSize, 1, "Reporting:BatchSize", errors);
        RequireAtLeast(reporting.FlushIntervalSeconds, 1, "Reporting:FlushIntervalSeconds", errors);
        RequireAtLeast(reporting.MaxAttempts, 1, "Reporting:MaxAttempts", errors);
        RequireAtLeast(reporting.CompleteTimeoutSeconds, 1, "Reporting:CompleteTimeoutSeconds", errors);

        if (string.IsNullOrWhiteSpace(reporting.RunName))
        {
            errors.Add("Reporting:RunName must not be empty.");
        }
        else
        {
            errors.AddRange(RunNameFormatter.Validate(reporting.RunName).Select(error => $"Reporting:RunName: {error}"));
        }

        if (reporting.RunDescription is not null)
        {
            errors.AddRange(RunNameFormatter.Validate(reporting.RunDescription).Select(error => $"Reporting:RunDescription: {error}"));
        }

        for (var index = 0; index < reporting.Systems.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(reporting.Systems[index]))
            {
                errors.Add($"Reporting:Systems:{index} is empty.");
            }
        }

        return errors;
    }

    // Unset: the first flow, or none. Throws UiAutomationSettingsException for a name no flow has.
    public static ISignInFlow? ResolveSignInFlow(TargetSettings target, IReadOnlyList<ISignInFlow> flows)
    {
        var error = TryResolveSignInFlow(target, flows, out var flow);
        return error is null ? flow : throw new UiAutomationSettingsException(error);
    }

    // Lowers what cannot work and warns about what may wait.
    public static void Adjust(UiAutomationSettings settings)
    {
        foreach (var key in SettingsRedaction.ShortSecretKeys(settings))
        {
            settings.AddWarning($"{key} has fewer than {SecretRedactor.MinimumLength} characters, too few to be redacted from logs and reports; use a longer secret.");
        }

        var poolSize = settings.Users.Pool.Count;
        if (poolSize == 0)
        {
            return;
        }

        var execution = settings.Execution;
        if (execution.MaxParallelTests > poolSize)
        {
            settings.AddWarning($"Execution:MaxParallelTests {execution.MaxParallelTests} is more than the {poolSize} users of Users:Pool; lowered to {poolSize}.");
            execution.MaxParallelTests = poolSize;
        }

        if (execution.MaxParallelTests * 2 > poolSize)
        {
            settings.AddWarning(
                $"Users:Pool has {poolSize} users for {execution.MaxParallelTests} parallel tests: a test that leases a second user may wait up to Users:LeaseTimeoutSeconds ({settings.Users.LeaseTimeoutSeconds} s).");
        }
    }

    private static string? TryResolveSignInFlow(TargetSettings target, IReadOnlyList<ISignInFlow> flows, out ISignInFlow? flow)
    {
        flow = null;
        var name = target.SignIn?.Trim();

        if (string.IsNullOrEmpty(name))
        {
            flow = flows.Count > 0 ? flows[0] : null;
            return null;
        }

        if (string.Equals(name, TargetSettings.NoSignIn, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        flow = flows.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (flow is not null)
        {
            return null;
        }

        var known = string.Join(", ", new[] { TargetSettings.NoSignIn }.Concat(flows.Select(candidate => candidate.Name)));
        return $"Target:SignIn '{name}' names no sign-in flow; use one of: {known}.";
    }

    private static void ValidateTarget(TargetSettings target, IUiAutomationSetup? setup, List<string> errors)
    {
        if (!Uri.TryCreate(target.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"Target:BaseUrl must be an absolute http or https URL; it is '{target.BaseUrl}'.");
        }

        if (setup is not null && TryResolveSignInFlow(target, setup.SignInFlows, out _) is { } error)
        {
            errors.Add(error);
        }
    }

    private static void ValidateBrowser(BrowserSettings browser, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(browser.Channel) && browser.Engine != BrowserEngine.Chromium)
        {
            errors.Add($"Browser:Channel '{browser.Channel}' needs Browser:Engine chromium; it is {browser.Engine.ToString().ToLowerInvariant()}.");
        }

        if (browser.SlowMoMs < 0)
        {
            errors.Add("Browser:SlowMoMs must not be negative.");
        }

        if (string.IsNullOrWhiteSpace(browser.Device) && (browser.Viewport.Width < 1 || browser.Viewport.Height < 1))
        {
            errors.Add("Browser:Viewport:Width and Browser:Viewport:Height must be positive.");
        }
    }

    private static void ValidateExecution(ExecutionSettings execution, List<string> errors)
    {
        RequireAtLeast(execution.MaxParallelTests, 1, "Execution:MaxParallelTests", errors);
        RequireAtLeast(execution.ActionTimeoutMs, 1, "Execution:ActionTimeoutMs", errors);
        RequireAtLeast(execution.NavigationTimeoutMs, 1, "Execution:NavigationTimeoutMs", errors);
        RequireAtLeast(execution.ExpectTimeoutMs, 1, "Execution:ExpectTimeoutMs", errors);

        if (execution.Retries is < 0 or > MaxRetries)
        {
            errors.Add($"Execution:Retries must be between 0 and {MaxRetries}.");
        }

        if (string.IsNullOrWhiteSpace(execution.ArtifactsPath))
        {
            errors.Add("Execution:ArtifactsPath must not be empty.");
        }
    }

    private static void ValidateUsers(UiAutomationSettings settings, IUiAutomationSetup? setup, List<string> errors)
    {
        var users = settings.Users;
        RequireAtLeast(users.LeaseTimeoutSeconds, 1, "Users:LeaseTimeoutSeconds", errors);

        ISignInFlow? flow = null;
        if (setup is not null)
        {
            TryResolveSignInFlow(settings.Target, setup.SignInFlows, out flow);
        }

        var emails = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < users.Pool.Count; index++)
        {
            var user = users.Pool[index];
            var email = user.Email?.Trim();

            if (string.IsNullOrEmpty(email))
            {
                errors.Add($"Users:Pool:{index}:Email is required.");
            }
            else if (!emails.TryAdd(email, index))
            {
                errors.Add($"Users:Pool:{index}:Email repeats Users:Pool:{emails[email]}:Email.");
            }

            if (flow is { RequiresPassword: true } && string.IsNullOrEmpty(user.Password))
            {
                errors.Add($"Users:Pool:{index}:Password is required by the sign-in flow {flow.Name}; set it in the user secrets or UITESTS_Users__Pool__{index}__Password.");
            }
        }
    }

    private static void ValidateBrowserStack(BrowserStackSettings browserStack, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(browserStack.Username))
        {
            errors.Add("BrowserStack:Username is required when Browser:Provider is BrowserStack; set it in the user secrets or UITESTS_BrowserStack__Username.");
        }

        if (string.IsNullOrWhiteSpace(browserStack.AccessKey))
        {
            errors.Add("BrowserStack:AccessKey is required when Browser:Provider is BrowserStack; set it in the user secrets or UITESTS_BrowserStack__AccessKey.");
        }

        if (browserStack.GetEngine() is null)
        {
            errors.Add($"BrowserStack:Browser '{browserStack.Browser}' is not one of: {string.Join(", ", BrowserStackSettings.Browsers.Keys)}.");
        }

        // The endpoint is shown without its query: a pasted caps URL holds the access key.
        if (!Uri.TryCreate(browserStack.Endpoint, UriKind.Absolute, out var endpoint) || (endpoint.Scheme != Uri.UriSchemeWs && endpoint.Scheme != Uri.UriSchemeWss))
        {
            errors.Add($"BrowserStack:Endpoint must be an absolute ws or wss URL; it is '{BrowserStackCapabilities.Describe(browserStack.Endpoint ?? string.Empty)}'.");
        }
        else if (endpoint.Query.Contains("caps=", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("BrowserStack:Endpoint must not have a caps parameter; the provider adds it from the BrowserStack section.");
        }

        if (string.IsNullOrWhiteSpace(browserStack.Os) || string.IsNullOrWhiteSpace(browserStack.OsVersion))
        {
            errors.Add("BrowserStack:Os and BrowserStack:OsVersion are required, e.g. Windows and 11, or OS X and Tahoe.");
        }

        if (!BrowserStackSettings.ConsoleLevels.Contains(browserStack.Console, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"BrowserStack:Console '{browserStack.Console}' is not one of: {string.Join(", ", BrowserStackSettings.ConsoleLevels)}.");
        }

        if (browserStack.Build is not null)
        {
            errors.AddRange(RunNameFormatter.Validate(browserStack.Build).Select(error => $"BrowserStack:Build: {error}"));
        }

        foreach (var key in browserStack.AdditionalCapabilities.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                errors.Add("BrowserStack:AdditionalCapabilities has an empty key.");
            }
            else if (BrowserStackCapabilities.IsCredential(key))
            {
                errors.Add($"BrowserStack:AdditionalCapabilities must not set {key}; use BrowserStack:Username and BrowserStack:AccessKey.");
            }
        }

        RequireAtLeast(browserStack.MaxParallelSessions, 1, "BrowserStack:MaxParallelSessions", errors);
        RequireAtLeast(browserStack.ConnectTimeoutSeconds, 1, "BrowserStack:ConnectTimeoutSeconds", errors);
    }

    private static void RequireAtLeast(int value, int minimum, string name, List<string> errors)
    {
        if (value < minimum)
        {
            errors.Add(minimum == 1 ? $"{name} must be at least 1." : $"{name} must be at least {minimum}.");
        }
    }
}
