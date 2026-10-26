using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.DbCreator.Configuration;
using Colorful;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.DbCreator.Logic;

public class GetBaseAnnotationsReview(IAnnotationService annotations, IOptions<BaseAccountOptions> baseAccount)
{
    public async Task RenderReview()
    {
        var response = await annotations.GetAnnotationsAsync(new AnnotationsRequest
        {
            Types = new[]
            {
                AnnotationType.Responsibility,
                AnnotationType.Subject,
                AnnotationType.Usage,
                AnnotationType.Context,
                AnnotationType.Execution,
            },
            Organization = Constants.ORGANIZATION,
        });

        Console.WriteLine();
        Console.WriteLine($"Annotations count: {response.Count}");
        foreach (var annotation in response.Annotations)
        {
            Console.WriteLine(annotation.GetFullKey(Constants.ORGANIZATION));
        }

        List<Annotation> responsibilities = await annotations.GetAnnotationsAsync(new AnnotationsRequest
        {
            Types = new[]
            {
                AnnotationType.Responsibility,
            },
            Organization = Constants.ORGANIZATION,
        });

        Console.WriteLine();
        Console.WriteLine($"Responsibilities count: {responsibilities.Count}");
        foreach (var responsibility in responsibilities)
        {
            Console.WriteLine(responsibility.AnnotationKey);
        }

        Console.WriteLine();
        Console.WriteLine($"Owner account {Constants.BASE_ACCOUNT_USER_NAME} with id #{baseAccount.Value.GetAccountId()}");

        if (baseAccount.Value.UsesLegacyAccountId())
        {
            Console.WriteLine(
                $"  The id is the legacy default, which does not resolve under Entra ID: set {BaseAccountOptions.SECTION}:id to the owner's {{tid}}:{{oid}}.",
                Color.Yellow);
        }
    }
}
