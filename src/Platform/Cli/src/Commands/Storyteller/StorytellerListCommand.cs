using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands.Configuration;
using _42.Platform.Storyteller;
using ApiSdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Storyteller;

[Subcommand(
    typeof(StorytellerGetCommand),
    typeof(StorytellerSetCommand),
    typeof(StorytellerDeleteCommand),
    typeof(ConfigGetCommand))]

[Command(CommandNames.STORYTELLER, CommandNames.STORY, CommandNames.ANNOTATIONS, Description = "Retrieve the story of your platform (manage annotations).")]
public class StorytellerListCommand : BaseContextCommand
{
    private readonly ApiClient _apiClient;

    public StorytellerListCommand(
        IExtendedConsole console,
        ApiClient apiClient,
        ICommandContext context)
        : base(console, context)
    {
        _apiClient = apiClient;
    }

    [Option("-r|--responsibilities", CommandOptionType.SingleValue, Description = "Query responsibilities by name.")]
    public string? QueryResponsibilities { get; set; }

    [Option("-s|--subjects", CommandOptionType.SingleValue, Description = "Query subjects by name.")]
    public string? QuerySubjects { get; set; }

    [Option("-u|--usages", CommandOptionType.NoValue, Description = "Query usages, can be combined with responsibilities or subjects.")]
    public bool QueryUsages { get; set; }

    [Option("-c|--contexts", CommandOptionType.SingleValue, Description = "Query contexts by name.")]
    public string? QueryContexts { get; set; }

    [Option("-e|--executions", CommandOptionType.NoValue, Description = "Query executions, can be combined with responsibilities, subjects, or contexts.")]
    public bool QueryExecutions { get; set; }

    [Option("-d|--descendants", CommandOptionType.SingleValue, Description = "Retrieve descendants of a specific annotation.")]
    public string? QueryDescendants { get; set; }

    [Option("-t|--tree", CommandOptionType.NoValue, Description = "Show results as tree based by subjects.")]
    public bool ShowAsTree { get; set; }

    [Option("-b|--tree-root", CommandOptionType.SingleValue, Description = "An annotation type to render the tree based on.")]
    public string? TreeBaseAnnotationType { get; set; }

    [Option("-k|--continuation-token", CommandOptionType.SingleValue, Description = "The continuation token for next results page.")]
    public string? ContinuationToken { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (ShowAsTree)
        {
            await RenderTreeAsync();
            return ExitCodes.SUCCESS;
        }

        if (!string.IsNullOrWhiteSpace(QueryDescendants)
            && AnnotationKey.TryParse(QueryDescendants, out var annotationKey))
        {
            await RenderDescendantsAsync(annotationKey);
            return ExitCodes.SUCCESS;
        }

        var hasCustomQuery = false;

        if (!string.IsNullOrWhiteSpace(QuerySubjects))
        {
            hasCustomQuery = true;
            await RenderSubjectsAsync();
        }

        if (!string.IsNullOrWhiteSpace(QueryResponsibilities))
        {
            hasCustomQuery = true;
            await RenderResponsibilitiesAsync();
        }

        if (QueryUsages)
        {
            hasCustomQuery = true;
            await RenderUsagesAsync();
        }

        if (!string.IsNullOrWhiteSpace(QueryContexts))
        {
            hasCustomQuery = true;
            await RenderContextsAsync();
        }

        if (QueryExecutions)
        {
            hasCustomQuery = true;
            await RenderExecutionsAsync();
        }

        if (!hasCustomQuery)
        {
            await RenderAnnotationsAsync();
        }

        return ExitCodes.SUCCESS;
    }

    private async Task RenderSubjectsAsync()
    {
        var response = await _apiClient.V1[Context.OrganizationName][Context.ProjectName][Context.ViewName].Subjects
            .GetAsync(rc =>
            {
                rc.QueryParameters.NameQuery = QuerySubjects;
                rc.QueryParameters.ContinuationToken = ContinuationToken;
            });

        Console.WriteHeader("Subjects");

        if (response?.Annotations is null || response.Annotations.Count < 1)
        {
            Console.WriteLine("No subject exists yet.".ThemedLowlight(Console.Theme));
        }
        else
        {
            foreach (var annotation in response.Annotations)
            {
                Console.WriteLine($"- {annotation.AnnotationKey}");
            }
        }

        if (!string.IsNullOrWhiteSpace(response?.ContinuationToken))
        {
            Console.WriteLine();
            Console.WriteLine("More subjects available, use continuation token to retrieve next page:".ThemedLowlight(Console.Theme));
            Console.WriteLine($"  {response.ContinuationToken}");
        }

        Console.WriteLine();
    }

    private async Task RenderResponsibilitiesAsync()
    {
        var response = await _apiClient.V1[Context.OrganizationName][Context.ProjectName][Context.ViewName].Responsibilities
            .GetAsync(rc =>
            {
                rc.QueryParameters.NameQuery = QueryResponsibilities;
                rc.QueryParameters.ContinuationToken = ContinuationToken;
            });

        Console.WriteHeader("Responsibilities");

        if (response?.Annotations is null || response.Annotations.Count < 1)
        {
            Console.WriteLine("No responsibility exists yet.".ThemedLowlight(Console.Theme));
        }
        else
        {
            foreach (var annotation in response.Annotations)
            {
                Console.WriteLine($"- {annotation.AnnotationKey}");
            }
        }

        if (!string.IsNullOrWhiteSpace(response?.ContinuationToken))
        {
            Console.WriteLine();
            Console.WriteLine("More responsibilities available, use continuation token to retrieve next page:".ThemedLowlight(Console.Theme));
            Console.WriteLine($"  {response.ContinuationToken}");
        }

        Console.WriteLine();
    }

    private async Task RenderUsagesAsync()
    {
        var response = await _apiClient.V1[Context.OrganizationName][Context.ProjectName][Context.ViewName].Usages
            .GetAsync(rc =>
            {
                rc.QueryParameters.ResponsibilityNameQuery = QueryResponsibilities;
                rc.QueryParameters.SubjectNameQuery = QuerySubjects;
                rc.QueryParameters.ContinuationToken = ContinuationToken;
            });

        Console.WriteHeader("Usages");

        if (response?.Annotations is null || response.Annotations.Count < 1)
        {
            Console.WriteLine("No usage exists yet.".ThemedLowlight(Console.Theme));
        }
        else
        {
            foreach (var annotation in response.Annotations)
            {
                Console.WriteLine($"- {annotation.AnnotationKey}");
            }
        }

        if (!string.IsNullOrWhiteSpace(response?.ContinuationToken))
        {
            Console.WriteLine();
            Console.WriteLine("More usages available, use continuation token to retrieve next page:".ThemedLowlight(Console.Theme));
            Console.WriteLine($"  {response.ContinuationToken}");
        }

        Console.WriteLine();
    }

    private async Task RenderContextsAsync()
    {
        var response = await _apiClient.V1[Context.OrganizationName][Context.ProjectName][Context.ViewName].Contexts
            .GetAsync(rc =>
            {
                rc.QueryParameters.SubjectNameQuery = QuerySubjects;
                rc.QueryParameters.NameQuery = QueryContexts;
                rc.QueryParameters.ContinuationToken = ContinuationToken;
            });

        Console.WriteHeader("Contexts");

        if (response?.Annotations is null || response.Annotations.Count < 1)
        {
            Console.WriteLine("No context exists yet.".ThemedLowlight(Console.Theme));
        }
        else
        {
            foreach (var annotation in response.Annotations)
            {
                Console.WriteLine($"- {annotation.AnnotationKey}");
            }
        }

        if (!string.IsNullOrWhiteSpace(response?.ContinuationToken))
        {
            Console.WriteLine();
            Console.WriteLine("More contexts available, use continuation token to retrieve next page:".ThemedLowlight(Console.Theme));
            Console.WriteLine($"  {response.ContinuationToken}");
        }

        Console.WriteLine();
    }

    private async Task RenderExecutionsAsync()
    {
        var response = await _apiClient.V1[Context.OrganizationName][Context.ProjectName][Context.ViewName].Executions
            .GetAsync(rc =>
            {
                rc.QueryParameters.SubjectNameQuery = QuerySubjects;
                rc.QueryParameters.ResponsibilityNameQuery = QueryResponsibilities;
                rc.QueryParameters.ContextNameQuery = QueryContexts;
                rc.QueryParameters.ContinuationToken = ContinuationToken;
            });

        Console.WriteHeader("Executions");

        if (response?.Annotations is null || response.Annotations.Count < 1)
        {
            Console.WriteLine("No execution exists yet.".ThemedLowlight(Console.Theme));
        }
        else
        {
            foreach (var annotation in response.Annotations)
            {
                Console.WriteLine($"- {annotation.AnnotationKey}");
            }
        }

        if (!string.IsNullOrWhiteSpace(response?.ContinuationToken))
        {
            Console.WriteLine();
            Console.WriteLine("More executions available, use continuation token to retrieve next page:".ThemedLowlight(Console.Theme));
            Console.WriteLine($"  {response.ContinuationToken}");
        }

        Console.WriteLine();
    }

    private async Task RenderDescendantsAsync(AnnotationKey annotationKey)
    {
        var response = await _apiClient.V1[Context.OrganizationName][Context.ProjectName][Context.ViewName].Annotations[annotationKey.ToString()]["all"]
            .GetAsync(rc =>
            {
                rc.QueryParameters.ContinuationToken = ContinuationToken;
            });

        Console.WriteHeader("All descendants");

        if (response?.Annotations is null || response.Annotations.Count < 1)
        {
            Console.WriteLine("No descendant exists yet.".ThemedLowlight(Console.Theme));
        }
        else
        {
            foreach (var annotation in response.Annotations)
            {
                Console.WriteLine($"- {annotation.AnnotationKey}");
            }
        }

        if (!string.IsNullOrWhiteSpace(response?.ContinuationToken))
        {
            Console.WriteLine();
            Console.WriteLine("More descendants available, use continuation token to retrieve next page:".ThemedLowlight(Console.Theme));
            Console.WriteLine($"  {response.ContinuationToken}");
        }
    }

    private async Task RenderAnnotationsAsync()
    {
        var response = await _apiClient.V1[Context.OrganizationName][Context.ProjectName][Context.ViewName].Annotations
            .GetAsync(rc => rc.QueryParameters.ContinuationToken = ContinuationToken);
        Console.WriteHeader("Annotations");

        if (response?.Annotations is null || response.Annotations.Count < 1)
        {
            Console.WriteLine("No annotation exists yet.".ThemedLowlight(Console.Theme));
        }
        else
        {
            foreach (var annotation in response.Annotations)
            {
                Console.WriteLine($"- {annotation.AnnotationKey}");
            }
        }

        if (!string.IsNullOrWhiteSpace(response?.ContinuationToken))
        {
            Console.WriteLine();
            Console.WriteLine("More annotations available, use continuation token to retrieve next page:".ThemedLowlight(Console.Theme));
            var token = response.ContinuationToken;
            Console.WriteLine($"  {token}");
        }
    }

    private async Task RenderTreeAsync()
    {
        if (!string.IsNullOrWhiteSpace(TreeBaseAnnotationType)
            && (string.Equals(TreeBaseAnnotationType, "responsibility", StringComparison.OrdinalIgnoreCase)
                || string.Equals(TreeBaseAnnotationType, AnnotationTypeCodes.Responsibility, StringComparison.OrdinalIgnoreCase)))
        {
            await RenderResponsibilityTreeAsync();
        }
        else
        {
            await RenderSubjectTreeAsync();
        }
    }

    private async Task RenderResponsibilityTreeAsync()
    {
        var response = await _apiClient.V1[Context.OrganizationName][Context.ProjectName][Context.ViewName].Responsibilities
            .GetAsync(rc =>
            {
                rc.QueryParameters.NameQuery = QueryResponsibilities;
                rc.QueryParameters.ContinuationToken = ContinuationToken;
            });

        var root = new Composition("Tree of responsibilities");

        if (response?.Annotations is null || response.Annotations.Count < 1)
        {
            root.Children.Add(new Composition(new ConsoleOutputThemedText(
                "No responsibility exists yet.",
                t => t.LowlightColor)));
        }
        else
        {
            foreach (var responsibility in response.Annotations)
            {
                var mapOfSubjects = new Dictionary<string, Composition>();

                var responsibilityNode = new Composition(new ConsoleOutputComposition(
                    new ConsoleOutput(responsibility.Name),
                    new ConsoleOutputThemedText(" [responsibility]", t => t.LowlightColor)));
                root.Children.Add(responsibilityNode);

                var executionsResponse = await _apiClient.V1[Context.OrganizationName][Context.ProjectName][Context.ViewName].Executions
                    .GetAsync(rc =>
                    {
                        rc.QueryParameters.SubjectNameQuery = QuerySubjects;
                        rc.QueryParameters.ResponsibilityNameQuery = responsibility.Name;
                        rc.QueryParameters.ContextNameQuery = QueryContexts;
                    });

                if (executionsResponse?.Annotations is not null)
                {
                    foreach (var execution in executionsResponse.Annotations)
                    {
                        var key = AnnotationKey.Parse(execution.AnnotationKey!);
                        var subjectName = key.SubjectName;

                        if (!mapOfSubjects.TryGetValue(subjectName, out var subjectNode))
                        {
                            subjectNode = new Composition(new ConsoleOutputComposition(
                                new ConsoleOutput(subjectName),
                                new ConsoleOutputThemedText(" [subject]", t => t.LowlightColor)));
                            mapOfSubjects.Add(subjectName, subjectNode);
                            responsibilityNode.Children.Add(subjectNode);
                        }

                        subjectNode.Children.Add(new Composition(new ConsoleOutputComposition(
                            new ConsoleOutput(key.ContextName),
                            new ConsoleOutputThemedText(" [context]", t => t.LowlightColor))));
                    }
                }

                foreach (var childNode in responsibilityNode.Children
                             .Where(node => node.Children.Count == 1))
                {
                    childNode.Children.Clear();
                }
            }
        }

        Console.WriteTree(root, node => node);

        if (!string.IsNullOrWhiteSpace(response?.ContinuationToken))
        {
            Console.WriteLine();
            Console.WriteLine("More responsibilities available, use continuation token to retrieve next page:".ThemedLowlight(Console.Theme));
            Console.WriteLine($"  {response.ContinuationToken}");
        }
    }

    private async Task RenderSubjectTreeAsync()
    {
        var response = await _apiClient.V1[Context.OrganizationName][Context.ProjectName][Context.ViewName].Subjects
            .GetAsync(rc =>
            {
                rc.QueryParameters.NameQuery = QuerySubjects;
                rc.QueryParameters.ContinuationToken = ContinuationToken;
            });

        var root = new Composition("Tree of subjects");

        if (response?.Annotations is null || response.Annotations.Count < 1)
        {
            root.Children.Add(new Composition(new ConsoleOutputThemedText(
                "No subject exists yet.",
                t => t.LowlightColor)));
        }
        else
        {
            foreach (var subject in response.Annotations)
            {
                var mapOfContexts = new Dictionary<string, Composition>();
                var subjectNode = new Composition(new ConsoleOutputComposition(
                    new ConsoleOutput(subject.Name),
                    new ConsoleOutputThemedText(" [subject]", t => t.LowlightColor)));
                root.Children.Add(subjectNode);

                var executionsResponse = await _apiClient.V1[Context.OrganizationName][Context.ProjectName][Context.ViewName].Executions
                    .GetAsync(rc =>
                    {
                        rc.QueryParameters.SubjectNameQuery = subject.Name;
                        rc.QueryParameters.ResponsibilityNameQuery = QueryResponsibilities;
                        rc.QueryParameters.ContextNameQuery = QueryContexts;
                    });

                if (executionsResponse?.Annotations is not null)
                {
                    foreach (var execution in executionsResponse.Annotations)
                    {
                        var key = AnnotationKey.Parse(execution.AnnotationKey!);
                        var contextName = key.ContextName;

                        if (!mapOfContexts.TryGetValue(contextName, out var contextNode))
                        {
                            contextNode = new Composition(new ConsoleOutputComposition(
                                new ConsoleOutput(contextName),
                                new ConsoleOutputThemedText(" [context]", t => t.LowlightColor)));
                            mapOfContexts.Add(contextName, contextNode);
                            subjectNode.Children.Add(contextNode);
                        }

                        contextNode.Children.Add(new Composition(new ConsoleOutputComposition(
                            new ConsoleOutput(key.ResponsibilityName),
                            new ConsoleOutputThemedText(" [responsibility]", t => t.LowlightColor))));
                    }
                }
            }
        }

        Console.WriteTree(root, node => node);

        if (!string.IsNullOrWhiteSpace(response?.ContinuationToken))
        {
            Console.WriteLine();
            Console.WriteLine("More subjects available, use continuation token to retrieve next page:".ThemedLowlight(Console.Theme));
            Console.WriteLine($"  {response.ContinuationToken}");
        }
    }
}
