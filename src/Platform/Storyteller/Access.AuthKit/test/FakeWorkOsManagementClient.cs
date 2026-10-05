using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.AuthKit.UnitTests;

// Records management calls in order; never sends HTTP.
internal sealed class FakeWorkOsManagementClient : WorkOsManagementClient
{
    private int _secretCounter;

    public FakeWorkOsManagementClient()
        : base(new HttpClient(), Options.Create(new UserAuthenticationOptions { AuthKit = new AuthKitOptions { ApiKey = "sk_test" } }))
    {
    }

    public List<string> Calls { get; } = [];

    public WorkOsM2MApplicationCreate? CreatedApplication { get; private set; }

    public List<WorkOsClientSecret> Secrets { get; } = [];

    public Exception? CreateSecretFailure { get; set; }

    public bool DeleteApplicationResult { get; set; } = true;

    public override Task<WorkOsConnectApplication> CreateM2MApplicationAsync(WorkOsM2MApplicationCreate application, CancellationToken cancellationToken = default)
    {
        Calls.Add("create-application");
        CreatedApplication = application;
        return Task.FromResult(new WorkOsConnectApplication("conn_app_01", "client_m2m01", application.Name, application.OrganizationId, application.Scopes));
    }

    public override Task<bool> DeleteApplicationAsync(string applicationId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"delete-application:{applicationId}");
        return Task.FromResult(DeleteApplicationResult);
    }

    public override Task<WorkOsClientSecret> CreateClientSecretAsync(string applicationId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"create-secret:{applicationId}");

        if (CreateSecretFailure is not null)
        {
            throw CreateSecretFailure;
        }

        _secretCounter++;
        var secret = new WorkOsClientSecret($"secret_new{_secretCounter}", $"plain-secret-{_secretCounter}", "hint", DateTimeOffset.UtcNow);
        Secrets.Add(secret with { Secret = null });
        return Task.FromResult(secret);
    }

    public override Task<IReadOnlyList<WorkOsClientSecret>> ListClientSecretsAsync(string applicationId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"list-secrets:{applicationId}");
        return Task.FromResult<IReadOnlyList<WorkOsClientSecret>>(Secrets.ToList());
    }

    public override Task<bool> DeleteClientSecretAsync(string secretId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"delete-secret:{secretId}");
        Secrets.RemoveAll(secret => secret.Id == secretId);
        return Task.FromResult(true);
    }
}
