using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;

namespace _42.Platform.Cli.Commands.Assistance;

[Command(CommandNames.KEY, Description = "Manage your account and access to 2S platform services.")]
public class AssistKeyCommand : BaseCommand
{
    public AssistKeyCommand(IExtendedConsole console)
        : base(console)
    {
        // no operation
    }

    public override async Task<int> OnExecuteAsync()
    {
        Console.WriteLine("Annotation key use simple format of:");
        Console.WriteLine("    <type>.<name>".ThemedHighlight(Console.Theme));
        Console.WriteLine("Type is three characters length code and hierarchy is done by dots notation same like object path in C#.".ThemedLowlight(Console.Theme));
        Console.WriteLine();

        var annotationType = Console.Select(new SelectOptions<AnnotationType>
        {
            Message = "What type of annotation you need",
            Items = new[]
            {
                AnnotationType.Responsibility,
                AnnotationType.Job,
                AnnotationType.Subject,
                AnnotationType.Usage,
                AnnotationType.Context,
                AnnotationType.Execution,
            },
            DefaultValue = AnnotationType.Responsibility,
            TextSelector = item => $"{item:G}",
        });

        var key = AnnotationKey.CreateResponsibility("app");

        switch (annotationType)
        {
            case AnnotationType.Responsibility:
            {
                var responsibilityName = GetName("responsibility", "awesome-app");
                key = AnnotationKey.CreateResponsibility(responsibilityName);
                break;
                }

            case AnnotationType.Job:
            {
                var responsibilityName = GetName("responsibility", "my-application");
                var jobName = GetName("job", "unique-work");
                key = AnnotationKey.CreateJob(responsibilityName, jobName);
                break;
            }

            case AnnotationType.Subject:
            {
                var subjectName = GetName("subject", "best-customer");
                key = AnnotationKey.CreateSubject(subjectName);
                break;
            }

            case AnnotationType.Usage:
            {
                var subjectName = GetName("subject", "customer");
                var responsibilityName = GetName("responsibility", "my-app");
                key = AnnotationKey.CreateUsage(subjectName, responsibilityName);
                break;
            }

            case AnnotationType.Context:
            {
                var subjectName = GetName("subject", "owner");
                var contextName = GetName("context", "cool-variant");
                key = AnnotationKey.CreateContext(subjectName, contextName);
                break;
            }

            case AnnotationType.Execution:
            {
                var subjectName = GetName("subject", "customer");
                var responsibilityName = GetName("responsibility", "service");
                var contextName = GetName("context", "variant");
                key = AnnotationKey.CreateExecution(subjectName, responsibilityName, contextName);
                break;
            }
        }

        Console.WriteImportant("Your annotation key should be ", $"{key}".ThemedHighlight(Console.Theme));
        return ExitCodes.SUCCESS;
    }

    private string GetName(string type, string placeholder)
    {
        var name = Console.Input(new InputOptions<string>
        {
            Message = $"Name of the {type}",
            Placeholder = placeholder,
            DefaultValue = type,
            Validators = { ValidateName },
        });

        return name;
    }

    private ValidationResult? ValidateName(object? value)
    {
        var name = value?.ToString();

        if (string.IsNullOrWhiteSpace(name))
        {
            return new ValidationResult("Name can't be empty.");
        }

        if (name.Length > 50)
        {
            return new ValidationResult("Name is too long, max 50 characters.");
        }

        var regEx = new Regex(@"^[a-zA-Z0-9\-_]+$", RegexOptions.Compiled);

        if (!regEx.IsMatch(name))
        {
            return new ValidationResult("Name can contain only letters, numbers, dash and underscore.");
        }

        return ValidationResult.Success;
    }
}
