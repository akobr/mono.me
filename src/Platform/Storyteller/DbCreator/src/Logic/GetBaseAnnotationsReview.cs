using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using Colorful;

namespace _42.Platform.Storyteller.DbCreator.Logic;

public class GetBaseAnnotationsReview(IAnnotationService annotations)
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
        Console.WriteLine($"Owner account {Constants.BASE_ACCOUNT_USER_NAME} with id #{Constants.BASE_ACCOUNT_ID}");
    }
}
