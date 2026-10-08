using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Cli.Authentication;

// WorkOS AuthKit CLI Auth: the OAuth 2.0 device authorization grant against the User Management API.
// Public client, no secret. Refresh tokens rotate, so the stored one is replaced after every exchange.
public sealed class AuthKitAuthenticationService : IAuthenticationService, IDisposable
{
    private const string DeviceCodeGrant = "urn:ietf:params:oauth:grant-type:device_code";
    private const string OrganizationSelectionGrant = "urn:workos:oauth:grant-type:organization-selection";
    private const string RefreshTokenGrant = "refresh_token";

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FallbackTokenLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SlowDownStep = TimeSpan.FromSeconds(1);

    private readonly HttpClient _httpClient;
    private readonly ITokenStore _store;
    private readonly string _clientId;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AuthKitAuthenticationService(HttpClient httpClient, ITokenStore store, ResolvedAuthentication settings)
        : this(httpClient, store, settings, TimeProvider.System, (delay, cancellationToken) => Task.Delay(delay, cancellationToken))
    {
    }

    internal AuthKitAuthenticationService(
        HttpClient httpClient,
        ITokenStore store,
        ResolvedAuthentication settings,
        TimeProvider time,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= new Uri($"{settings.AuthKitApiBaseUrl.TrimEnd('/')}/");
        _store = store;
        _clientId = settings.ClientId;
        _time = time;
        _delay = delay;
    }

    public async Task<SignedInUser?> GetSignedInUserAsync(CancellationToken cancellationToken = default)
    {
        var session = await GetValidSessionAsync(cancellationToken);
        return session is null ? null : ToUser(session);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var session = await GetValidSessionAsync(cancellationToken);
        return session?.AccessToken;
    }

    public async Task<SignedInUser> LoginWithDeviceCodeAsync(
        Func<DeviceCodePrompt, Task> onPrompt,
        Func<IReadOnlyList<OrganizationChoice>, Task<string>>? selectOrganization = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await AuthorizeDeviceAsync(cancellationToken);
            var expiresIn = TimeSpan.FromSeconds(Math.Max(1, authorization.ExpiresIn));
            var deadline = _time.GetUtcNow() + expiresIn;
            var interval = authorization.Interval is > 0 ? TimeSpan.FromSeconds(authorization.Interval.Value) : DefaultPollInterval;

            await onPrompt(new DeviceCodePrompt(
                authorization.UserCode!,
                new Uri(authorization.VerificationUri!),
                Uri.TryCreate(authorization.VerificationUriComplete, UriKind.Absolute, out var complete) ? complete : null,
                expiresIn));

            while (true)
            {
                await _delay(interval, cancellationToken);

                if (_time.GetUtcNow() >= deadline)
                {
                    throw new AuthenticationException(AuthenticationFailureReason.Expired, "The sign-in code expired before the sign-in was completed.");
                }

                var outcome = await AuthenticateAsync(
                    new Dictionary<string, string>
                    {
                        ["grant_type"] = DeviceCodeGrant,
                        ["device_code"] = authorization.DeviceCode!,
                        ["client_id"] = _clientId,
                    },
                    cancellationToken);

                if (outcome.Session is not null)
                {
                    return await SaveAsync(outcome.Session, cancellationToken);
                }

                switch (outcome.Error)
                {
                    case "authorization_pending":
                        continue;
                    case "slow_down":
                        interval += SlowDownStep;
                        continue;
                    case "access_denied":
                        throw new AuthenticationException(AuthenticationFailureReason.Denied, "The sign-in was declined.");
                    case "expired_token":
                        throw new AuthenticationException(AuthenticationFailureReason.Expired, "The sign-in code expired before the sign-in was completed.");
                    case "organization_selection_required":
                        return await SelectOrganizationAsync(outcome.Body!, selectOrganization, cancellationToken);
                    default:
                        throw ServiceError(outcome);
                }
            }
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new AuthenticationException(AuthenticationFailureReason.Cancelled, "The sign-in was cancelled.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new AuthenticationException(AuthenticationFailureReason.ServiceError, "WorkOS could not be reached.", exception);
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            using var processLock = await _store.LockAsync(cancellationToken);
            await _store.ClearAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }

    // Unverified read of exp; the API validates the token, sform only needs to know when to refresh.
    internal static DateTimeOffset? ReadExpiry(string accessToken)
    {
        var parts = accessToken.Split('.');

        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            using var payload = JsonDocument.Parse(DecodeBase64Url(parts[1]));
            return payload.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
    }

    private static SignedInUser ToUser(AuthKitSession session)
    {
        var user = session.User;
        var name = string.Join(' ', new[] { user.FirstName, user.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return new SignedInUser(user.Id, user.Email ?? user.Id, name.Length > 0 ? name : null);
    }

    private static AuthenticationException ServiceError(AuthenticateOutcome outcome)
    {
        var detail = outcome.Body?.Description ?? outcome.Error ?? $"HTTP {(int)outcome.Status}";
        return new AuthenticationException(AuthenticationFailureReason.ServiceError, $"WorkOS rejected the sign-in: {detail}");
    }

    private async Task<AuthKitSession?> GetValidSessionAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var session = await _store.ReadAsync(cancellationToken);

            if (session is null || IsFresh(session))
            {
                return session;
            }

            using var processLock = await _store.LockAsync(cancellationToken);

            // Another sform process may have rotated the refresh token while this one waited.
            session = await _store.ReadAsync(cancellationToken);

            if (session is null || IsFresh(session))
            {
                return session;
            }

            var refreshed = await RefreshAsync(session, cancellationToken);

            if (refreshed is null)
            {
                await _store.ClearAsync(cancellationToken);
                return null;
            }

            await _store.WriteAsync(refreshed, cancellationToken);
            return refreshed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsFresh(AuthKitSession session)
    {
        return session.AccessTokenExpiresAt - RefreshMargin > _time.GetUtcNow();
    }

    // Null when WorkOS no longer accepts the refresh token, so the user has to sign in again.
    private async Task<AuthKitSession?> RefreshAsync(AuthKitSession session, CancellationToken cancellationToken)
    {
        AuthenticateOutcome outcome;

        try
        {
            outcome = await AuthenticateAsync(
                new Dictionary<string, string>
                {
                    ["grant_type"] = RefreshTokenGrant,
                    ["refresh_token"] = session.RefreshToken,
                    ["client_id"] = _clientId,
                },
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new AuthenticationException(AuthenticationFailureReason.ServiceError, "WorkOS could not be reached to refresh the session.", exception);
        }

        if (outcome.Session is not null)
        {
            return outcome.Session with { OrganizationId = outcome.Session.OrganizationId ?? session.OrganizationId };
        }

        // invalid_grant and other client errors: the token is revoked, expired or already used.
        if (outcome.Status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return null;
        }

        throw ServiceError(outcome);
    }

    private async Task<SignedInUser> SelectOrganizationAsync(
        ErrorResponse error,
        Func<IReadOnlyList<OrganizationChoice>, Task<string>>? selectOrganization,
        CancellationToken cancellationToken)
    {
        var organizations = (error.Organizations ?? [])
            .Where(organization => !string.IsNullOrWhiteSpace(organization.Id))
            .Select(organization => new OrganizationChoice(organization.Id!, organization.Name ?? organization.Id!))
            .ToList();

        if (organizations.Count == 0 || string.IsNullOrWhiteSpace(error.PendingAuthenticationToken))
        {
            throw new AuthenticationException(AuthenticationFailureReason.ServiceError, "WorkOS asked for an organization but offered none.");
        }

        var organizationId = selectOrganization is null
            ? organizations[0].Id
            : await selectOrganization(organizations);

        var outcome = await AuthenticateAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = OrganizationSelectionGrant,
                ["pending_authentication_token"] = error.PendingAuthenticationToken,
                ["organization_id"] = organizationId,
                ["client_id"] = _clientId,
            },
            cancellationToken);

        return outcome.Session is not null
            ? await SaveAsync(outcome.Session, cancellationToken)
            : throw ServiceError(outcome);
    }

    private async Task<SignedInUser> SaveAsync(AuthKitSession session, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            using var processLock = await _store.LockAsync(cancellationToken);
            await _store.WriteAsync(session, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        return ToUser(session);
    }

    private async Task<DeviceAuthorizationResponse> AuthorizeDeviceAsync(CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = _clientId });
        using var response = await _httpClient.PostAsync("user_management/authorize/device", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, cancellationToken);
            throw ServiceError(new AuthenticateOutcome(null, error?.Code, error, response.StatusCode));
        }

        var authorization = await ReadSuccessAsync<DeviceAuthorizationResponse>(response, cancellationToken);

        if (string.IsNullOrWhiteSpace(authorization?.DeviceCode)
            || string.IsNullOrWhiteSpace(authorization.UserCode)
            || !Uri.IsWellFormedUriString(authorization.VerificationUri, UriKind.Absolute))
        {
            throw new AuthenticationException(AuthenticationFailureReason.ServiceError, "WorkOS returned an incomplete device authorization.");
        }

        return authorization;
    }

    private async Task<AuthenticateOutcome> AuthenticateAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync("user_management/authenticate", content, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var body = await ReadSuccessAsync<AuthenticateResponse>(response, cancellationToken);

            if (body?.User is null || string.IsNullOrWhiteSpace(body.AccessToken) || string.IsNullOrWhiteSpace(body.RefreshToken))
            {
                throw new AuthenticationException(AuthenticationFailureReason.ServiceError, "WorkOS returned an incomplete session.");
            }

            var session = new AuthKitSession
            {
                AccessToken = body.AccessToken,
                RefreshToken = body.RefreshToken,
                AccessTokenExpiresAt = ReadExpiry(body.AccessToken) ?? _time.GetUtcNow() + FallbackTokenLifetime,
                User = body.User,
                OrganizationId = body.OrganizationId,
            };

            return new AuthenticateOutcome(session, null, null, response.StatusCode);
        }

        var error = await ReadErrorAsync(response, cancellationToken);
        return new AuthenticateOutcome(null, error?.Code, error, response.StatusCode);
    }

    private static async Task<T?> ReadSuccessAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new AuthenticationException(AuthenticationFailureReason.ServiceError, "WorkOS returned a response sform cannot read.", exception);
        }
    }

    private static async Task<ErrorResponse?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            // Not a JSON body.
            return null;
        }
    }

    private sealed record AuthenticateOutcome(AuthKitSession? Session, string? Error, ErrorResponse? Body, HttpStatusCode Status);

    private sealed record DeviceAuthorizationResponse(
        [property: JsonPropertyName("device_code")] string? DeviceCode,
        [property: JsonPropertyName("user_code")] string? UserCode,
        [property: JsonPropertyName("verification_uri")] string? VerificationUri,
        [property: JsonPropertyName("verification_uri_complete")] string? VerificationUriComplete,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("interval")] int? Interval);

    private sealed record AuthenticateResponse(
        [property: JsonPropertyName("user")] AuthKitSessionUser? User,
        [property: JsonPropertyName("organization_id")] string? OrganizationId,
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken);

    // Device-code errors use OAuth's error / error_description; other WorkOS errors use code / message.
    private sealed record ErrorResponse(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription,
        [property: JsonPropertyName("code")] string? ErrorCode,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("pending_authentication_token")] string? PendingAuthenticationToken,
        [property: JsonPropertyName("organizations")] List<OrganizationEntry>? Organizations)
    {
        [JsonIgnore]
        public string? Code => Error ?? ErrorCode;

        [JsonIgnore]
        public string? Description => ErrorDescription ?? Message ?? Code;
    }

    private sealed record OrganizationEntry(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name);
}
