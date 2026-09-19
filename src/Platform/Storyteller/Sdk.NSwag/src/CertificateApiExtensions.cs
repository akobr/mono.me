using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Storyteller.Sdk;

/// <summary>
/// Manual API methods for mTLS certificate endpoints.
/// These will be replaced by NSwag-generated code when the OpenAPI spec is regenerated.
/// </summary>
public partial class AccessApiClient
{
    private static readonly JsonSerializerOptions CertJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<string> GetCertificateAuthorityPemAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/v1/access/certificate-authority");
        PrepareRequest(_httpClient, request, request.RequestUri!.ToString());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task SetMachineAuthenticationPolicyAsync(
        string accessPointKey,
        MachineAuthenticationPolicyDto policy,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(policy, CertJsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/v1/access/points/{accessPointKey}/machine-authentication")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        PrepareRequest(_httpClient, request, request.RequestUri!.ToString());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<CertificateRenewalResponse> RenewMachineCertificateAsync(
        string organization,
        string project,
        string machineAccessId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/v1/{organization}/{project}/access/machines/{machineAccessId}/certificate/renew");
        PrepareRequest(_httpClient, request, request.RequestUri!.ToString());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<CertificateRenewalResponse>(body, CertJsonOptions)!;
    }

    public async Task<List<SharedCertificateDto>> GetSharedCertificatesAsync(
        string organization,
        string project,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/v1/{organization}/{project}/access/certificates");
        PrepareRequest(_httpClient, request, request.RequestUri!.ToString());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<List<SharedCertificateDto>>(body, CertJsonOptions) ?? [];
    }

    public async Task<SharedCertificateDto> IssueSharedCertificateAsync(
        string organization,
        string project,
        string label,
        int? lifetimeDays = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new { Label = label, LifetimeDays = lifetimeDays };
        var json = JsonSerializer.Serialize(payload, CertJsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/v1/{organization}/{project}/access/certificates")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        PrepareRequest(_httpClient, request, request.RequestUri!.ToString());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<SharedCertificateDto>(body, CertJsonOptions)!;
    }

    public async Task RevokeSharedCertificateAsync(
        string organization,
        string project,
        string thumbprint,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete,
            $"{BaseUrl}/v1/{organization}/{project}/access/certificates/{thumbprint}");
        PrepareRequest(_httpClient, request, request.RequestUri!.ToString());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

public class MachineAuthenticationPolicyDto
{
    public string CredentialKind { get; set; } = "ApiKey";

    public int? CertificateLifetimeDays { get; set; }
}

public class CertificateRenewalResponse
{
    public string? Thumbprint { get; set; }

    public string? Certificate { get; set; }

    public string? CertificatePassword { get; set; }

    public DateTimeOffset? LastRenewalAt { get; set; }

    public string? Message { get; set; }
}

public class SharedCertificateDto
{
    public string Thumbprint { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public DateTimeOffset NotBefore { get; set; }

    public DateTimeOffset NotAfter { get; set; }

    public bool IsRevoked { get; set; }

    public string? Certificate { get; set; }

    public string? CertificatePassword { get; set; }
}
