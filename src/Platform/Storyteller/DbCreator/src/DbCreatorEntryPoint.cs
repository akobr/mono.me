using System;
using System.IO.Abstractions;
using _42.Platform.Storyteller.DbCreator.Configuration;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.DbCreator.Logic;
using _42.Platform.Storyteller.Transferring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace _42.Platform.Storyteller.DbCreator;

public static class DbCreatorEntryPoint
{
    // The tool without its host: the options, the importer, the data sets and the structure builder. Call it after
    // AddCosmosDbAnnotations, whose services it uses; the test fixtures of the Cosmos DB suites call it too.
    public static IServiceCollection AddDbCreator(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BaseAccountOptions>(configuration.GetSection(BaseAccountOptions.SECTION));
        services.Configure<StagingSeedOptions>(configuration.GetSection(StagingSeedOptions.SECTION));

        services.TryAddSingleton<IFileSystem, FileSystem>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddViewImporter();

        services.TryAddSingleton<DataSetCatalog>();
        services.TryAddSingleton<IDataSetSeedRecordStore, CosmosDataSetSeedRecordStore>();
        services.TryAddSingleton<DataSetProvisioner>();
        services.TryAddSingleton<DataSetSeeder>();

        services.TryAddSingleton<CoreDbStructureBuilder>();
        services.TryAddSingleton<GetBaseAnnotationsReview>();

        return services;
    }
}
