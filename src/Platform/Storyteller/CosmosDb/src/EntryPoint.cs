using _42.Platform.Storyteller.Access;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Backend.Accessing;
using _42.Platform.Storyteller.Backend.Configuring;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.Access;
using _42.Platform.Storyteller.Entities.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller;

public static class EntryPoint
{
    public static IServiceCollection AddCosmosDbAnnotations(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CosmosDbOptions>(configuration.GetSection(CosmosDbOptions.SectionName));

        services.AddSingleton<ICosmosClientProvider, CosmosClientProvider>();
        services.AddSingleton<IContainerFactory, ContainerFactory>();
        services.AddSingleton<IContainerRepositoryProvider, ContainerRepositoryProvider>();

        services.AddSingleton<IAccessService, CosmosAccessService>();
        services.AddSingleton<IAnnotationService, CosmosAnnotationService>();
        services.AddSingleton<IConfigurationService, CosmosConfigurationService>();

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

            config.CreateMap<Account, AccountEntity>().ReverseMap();
            config.CreateMap<AccessPoint, AccessPointEntity>().ReverseMap();
            config.CreateMap<MachineAccess, MachineAccessEntity>().ReverseMap();
        });

        return services;
    }
}
