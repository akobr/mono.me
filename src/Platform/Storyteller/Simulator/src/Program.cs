using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Simulator;

public class Program
{
    private const string organization = "42dotnet";

    public static async Task<int> Main(string[] args)
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<ICosmosClientProvider, CosmosClientProvider>();
        collection.AddSingleton<IContainerFactory, ContainerFactory>();
        collection.AddSingleton<IContainerRepositoryProvider, ContainerRepositoryProvider>();
        collection.AddSingleton<IAnnotationService, CosmosAnnotationService>();

        var services = collection.BuildServiceProvider();
        var containerFactory = services.GetRequiredService<IContainerFactory>();

        var coreContainer = await containerFactory.CreateContainerIfNotExistsAsync("core");
        var houseContainer = await containerFactory.CreateContainerIfNotExistsAsync($"org.{organization}");

        await Data.CreateCore(houseContainer);
        Console.WriteLine("2S platform core structure created.");

        var service = services.GetRequiredService<IAnnotationService>();

        var response = await service.GetAnnotationsAsync(new AnnotationsRequest
        {
            Types = new[]
            {
                AnnotationType.Responsibility,
                AnnotationType.Subject,
                AnnotationType.Usage,
                AnnotationType.Context,
                AnnotationType.Execution,
            },
            Organization = organization,
        });

        Console.WriteLine();
        Console.WriteLine($"Annotations count: {response.Count}");

        foreach (var annotation in response.Annotations)
        {
            Console.WriteLine(annotation.GetFullKey(organization));
        }

        List<Annotation> responsibilities = await service.GetAnnotationsAsync(new AnnotationsRequest
        {
            Types = new[]
            {
                AnnotationType.Responsibility,
            },
            Organization = organization,
        });

        Console.WriteLine();
        Console.WriteLine($"Responsibilities count: {responsibilities.Count}");
        foreach (var responsibility in responsibilities)
        {
            Console.WriteLine(responsibility.AnnotationKey);
        }

        return 0;
    }
}
