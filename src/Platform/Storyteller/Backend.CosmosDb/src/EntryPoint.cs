using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Json;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Audit;
using _42.Platform.Storyteller.KeyValues.Policy;
using _42.Platform.Storyteller.KeyValues.Protection;
using _42.Platform.Storyteller.KeyValues.Usage;
using _42.Platform.Storyteller.Transferring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

using JsonConverter = Newtonsoft.Json.JsonConverter;

namespace _42.Platform.Storyteller;

public static class EntryPoint
{
    public static IServiceCollection AddCosmosDbAnnotations(this IServiceCollection services, IConfiguration configuration, string? connectionName = null)
    {
        services.Configure<CosmosDbOptions>(options =>
        {
            configuration.GetSection(CosmosDbOptions.SectionName).Bind(options);

            if (!string.IsNullOrWhiteSpace(connectionName))
            {
                var connectionString = configuration.GetConnectionString(connectionName);

                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    options.Connection = connectionString;
                }
            }

            if (string.IsNullOrWhiteSpace(options.Connection))
            {
                options.Connection = configuration.GetConnectionString("cosmos") ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(options.Connection)
                && (options.Connection.Contains("localhost") || options.Connection.Contains("127.0.0.1")))
            {
                options.ShouldAcceptAnyCertificate ??= true;
            }
        });

        services.Configure<JsonSerializerOptions>(options =>
        {
            options.PropertyNamingPolicy = new NoChangeNamingPolicy();
            options.Converters.Add(new JsonStringEnumConverter());
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.PropertyNameCaseInsensitive = true;
            options.AllowTrailingCommas = true;
        });

        services.Configure<JsonSerializerSettings>(settings =>
        {
            settings.ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new DefaultNamingStrategy(),
                IgnoreSerializableAttribute = true,
            };
            settings.Converters = new List<JsonConverter>
            {
                new StringEnumConverter(new DefaultNamingStrategy(), true),
            };
            settings.NullValueHandling = NullValueHandling.Ignore;
            settings.ObjectCreationHandling = ObjectCreationHandling.Replace;
            settings.ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor;
            settings.MissingMemberHandling = MissingMemberHandling.Ignore;
            settings.DateFormatHandling = DateFormatHandling.IsoDateFormat;
            settings.DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind;
            settings.Formatting = Formatting.None;
            settings.DefaultValueHandling = DefaultValueHandling.Include;
            settings.TypeNameHandling = TypeNameHandling.None;
        });

        services.AddSingleton<JsonSerializationSettingsService>();
        services.AddSingleton<IJsonSerializationSettingsRegistry>(
            p => p.GetRequiredService<JsonSerializationSettingsService>());
        services.AddSingleton<IJsonSerializationSettingsProvider>(
            p => p.GetRequiredService<JsonSerializationSettingsService>());

        services.AddSingleton<ICosmosClientProvider, CosmosClientProvider>();
        services.AddSingleton<IContainerFactory, ContainerFactory>();
        services.AddSingleton<IContainerRepositoryProvider, ContainerRepositoryProvider>();

        services.AddSingleton<IApiKeyHashStore, CosmosMergedApiKeyHashStore>();
        services.AddSingleton<ICertificateAuthorityStore, CosmosCertificateAuthorityStore>();
        services.AddSingleton<IMachineAuthenticationPolicyStore, CosmosMachineAuthenticationPolicyStore>();
        services.AddSingleton<ICertificateRenewalService, CosmosCertificateRenewalService>();
        services.AddSingleton<ISharedCertificateStore, CosmosSharedCertificateStore>();

        // Workload identity federation (AddFederatedMachineAccess keeps these and adds in-memory ones only without them).
        services.AddSingleton<IFederatedIssuerStore, CosmosFederatedIssuerStore>();
        services.AddSingleton<ITokenSigningKeyStore, CosmosTokenSigningKeyStore>();

        // Annotations: soft-deleted ones expire after the retention, every change is audited within a size limit.
        services.AddOptions<AnnotationOptions>()
            .Bind(configuration.GetSection(AnnotationOptions.SectionName))
            .Validate(
                options => options.DeletedRetentionDays is >= AnnotationOptions.MinDeletedRetentionDays and <= AnnotationOptions.MaxDeletedRetentionDays,
                $"{AnnotationOptions.SectionName}:{nameof(AnnotationOptions.DeletedRetentionDays)} must be between {AnnotationOptions.MinDeletedRetentionDays} and {AnnotationOptions.MaxDeletedRetentionDays}.");
        services.AddOptions<AnnotationAuditOptions>()
            .Bind(configuration.GetSection(AnnotationAuditOptions.SectionName))
            .Validate(
                options => options.MaxDocumentBytes is >= 1024 and <= AnnotationAuditOptions.MaxAllowedDocumentBytes && options.MaxValueLength >= 1,
                $"{AnnotationAuditOptions.SectionName}:{nameof(AnnotationAuditOptions.MaxDocumentBytes)} must be between 1 KiB and 1.5 MiB, and {nameof(AnnotationAuditOptions.MaxValueLength)} positive.");
        services.AddSingleton(provider => new AnnotationItemSerializer(
            provider.GetRequiredService<IOptions<JsonSerializerOptions>>().Value,
            provider.GetRequiredService<IOptions<JsonSerializerSettings>>().Value));
        services.AddSingleton<AnnotationAuditStore>();

        // Schemas: reading one and checking a document against it are bounded in time.
        services.AddOptions<ConfigurationSchemaOptions>()
            .Bind(configuration.GetSection(ConfigurationSchemaOptions.SectionName))
            .Validate(
                options => options.PatternMatchTimeout >= ConfigurationSchemaOptions.MinPatternMatchTimeout
                    && options.PatternMatchTimeout <= ConfigurationSchemaOptions.MaxPatternMatchTimeout
                    && options.CheckBudget >= ConfigurationSchemaOptions.MinCheckBudget
                    && options.CheckBudget <= ConfigurationSchemaOptions.MaxCheckBudget,
                $"{ConfigurationSchemaOptions.SectionName}:{nameof(ConfigurationSchemaOptions.PatternMatchTimeout)} must be between 10 ms and 10 s, and {nameof(ConfigurationSchemaOptions.CheckBudget)} between 1 s and 5 min.");

        services.AddSingleton<IAccessService, CosmosAccessService>();
        services.AddSingleton<IAnnotationService, CosmosAnnotationService>();
        services.AddSingleton<IConfigurationService, CosmosConfigurationService>();
        services.AddSingleton<IConfigurationSchemaService, CosmosConfigurationSchemaService>();
        services.AddSingleton<IConfigurationTemplateService, CosmosConfigurationTemplateService>();
        services.AddSingleton<IViewService, CosmosViewService>();

        // Key/value pairs: the Key Vault providers of the protection levels are added by the deployment.
        services.Configure<KeyValueOptions>(configuration.GetSection(KeyValueOptions.SectionName));
        services.TryAddSingleton<IValueProtector, EnvelopeValueProtector>();
        services.AddSingleton<IKeyValueService, CosmosKeyValueService>();
        services.AddSingleton<IKeyValuePolicyStore, CosmosKeyValuePolicyStore>();
        services.AddSingleton<IKeyValuePolicyService, CosmosKeyValuePolicyService>();
        services.AddSingleton<IKeyValueMaintenanceService, CosmosKeyValueMaintenanceService>();
        services.AddSingleton<IKeyValueAuditLog, CosmosKeyValueAuditLog>();
        services.AddSingleton<IKeyValueUsageRecorder, KeyValueUsageRecorder>();
        services.AddSingleton<IKeyValueUsageStore, CosmosKeyValueUsageStore>();
        services.AddSingleton<IKeyValueUsageService, CosmosKeyValueUsageService>();

        // Machine certificates in Cosmos, so every instance recognizes them (AddCertificateMachineAccess keeps this one).
        services.AddSingleton<IClientCertificateStore, CosmosClientCertificateStore>();
        // Export and import jobs. The service is added by AddViewTransfers, the archives and the queue
        // by Backend.AzureStorage.
        services.AddSingleton<IViewTransferStore, CosmosViewTransferStore>();

        // Invitations: the identity provider may replace the sender (AuthKit sends the email through WorkOS).
        services.Configure<InvitationOptions>(configuration.GetSection(InvitationOptions.SectionName));
        services.TryAddSingleton<IInvitationSender, NoopInvitationSender>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddTransient<IInvitationService, CosmosInvitationService>();

        services.AddMemoryCache();

        return services;
    }
}
