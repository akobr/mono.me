using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.Access;
using _42.Platform.Storyteller.Entities.Annotations;
using _42.Platform.Storyteller.Entities.Configurations;
using _42.Platform.Storyteller.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddSingleton<IAccessService, CosmosAccessService>();
        services.AddSingleton<IAnnotationService, CosmosAnnotationService>();
        services.AddSingleton<IConfigurationService, CosmosConfigurationService>();
        services.AddSingleton<IConfigurationSchemaService, CosmosConfigurationSchemaService>();

        services.AddAutoMapper(config =>
        {
            config.AllowNullDestinationValues = true;
            config.AllowNullCollections = true;

            config.CreateMap<Responsibility, ResponsibilityEntity>().ReverseMap();
            config.CreateMap<Unit, UnitEntity>().ReverseMap();
            config.CreateMap<Subject, SubjectEntity>().ReverseMap();
            config.CreateMap<Context, ContextEntity>().ReverseMap();
            config.CreateMap<Usage, UsageEntity>().ReverseMap();
            config.CreateMap<Execution, ExecutionEntity>().ReverseMap();
            config.CreateMap<UnitOfExecution, UnitOfExecutionEntity>().ReverseMap();

            config.CreateMap<Account, AccountEntity>().ReverseMap();
            config.CreateMap<AccessPoint, AccessPointEntity>().ReverseMap();
            config.CreateMap<MachineAccess, MachineAccessEntity>().ReverseMap();

            config.CreateMap<ConfigurationEntity, ConfigurationVersion>()
                .ForMember(dest => dest.CreationTime, opt => opt.MapFrom(src => src.GetLastUpdatedTime()))
                .ForMember(dest => dest.ExpirationTime, opt => opt.MapFrom(_ => DateTimeOffset.MaxValue));

            config.CreateMap<ConfigurationHistoryEntity, ConfigurationVersion>()
                .ForMember(dest => dest.ExpirationTime, opt => opt.MapFrom(src => src.GetExpirationTime()));
        });

        return services;
    }
}
