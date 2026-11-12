using System.Reflection;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Configuring;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// A body that isn't JSON is the client's error: a 400 and a Warning, never an Error, which would be a Sentry event and
// spend the shared quota. The services are never called: the parsing answers first.
public class ClientParseFailureLoggingTests
{
    private const string Org = "acme";
    private const string Project = "billing";
    private const string View = "default";
    private const string Key = "rst.payments";
    private const string NotJson = "{ not json";

    public static TheoryData<string> Routes => new()
    {
        nameof(ConfigurationHttp.SetConfiguration),
        nameof(ConfigurationHttp.PatchConfiguration),
        nameof(ConfigurationSchemaHttp.SetConfigurationSchema),
        nameof(ConfigurationSchemaHttp.SetAnnotationSchema),
        nameof(ConfigurationSchemaHttp.SetDescendantTypeSchema),
        nameof(TemplateHttp.SetTemplate),
        nameof(TemplateHttp.PatchTemplate),
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Invoke_ABodyThatIsNotJson_AnswersBadRequestAndOnlyWarns(string route)
    {
        var (result, entries) = await InvokeAsync(route);

        var badRequest = result.ShouldBeOfType<BadRequestObjectResult>();
        badRequest.Value.ShouldBeOfType<ErrorResponse>().Message.ShouldStartWith("Invalid ");
        var entry = entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldNotBeNull();
    }

    private static async Task<(IActionResult Result, IReadOnlyCollection<CapturingLogger<object>.Entry> Entries)> InvokeAsync(string route)
    {
        var access = new RoleAccess(AccountRole.Contributor);

        switch (route)
        {
            case nameof(ConfigurationHttp.SetConfiguration):
            case nameof(ConfigurationHttp.PatchConfiguration):
            {
                var logger = new CapturingLogger<ConfigurationHttp>();
                var http = new ConfigurationHttp(
                    Untouched<IConfigurationService>(),
                    Untouched<IAnnotationService>(),
                    access,
                    new FixedKeyValuePolicies(requireMtls: false),
                    logger);
                var result = route == nameof(ConfigurationHttp.SetConfiguration)
                    ? await http.SetConfiguration(TestRequests.User("PUT", NotJson), Org, Project, View, Key)
                    : await http.PatchConfiguration(TestRequests.User("PATCH", NotJson), Org, Project, View, Key);
                return (result, Entries(logger.Entries));
            }

            case nameof(ConfigurationSchemaHttp.SetConfigurationSchema):
            case nameof(ConfigurationSchemaHttp.SetAnnotationSchema):
            case nameof(ConfigurationSchemaHttp.SetDescendantTypeSchema):
            {
                var logger = new CapturingLogger<ConfigurationSchemaHttp>();
                var http = new ConfigurationSchemaHttp(Untouched<IConfigurationSchemaService>(), access, logger);
                var request = TestRequests.User("PUT", NotJson);
                var result = route switch
                {
                    nameof(ConfigurationSchemaHttp.SetConfigurationSchema) => await http.SetConfigurationSchema(request, Org, Project, View, AnnotationTypeCodes.Execution),
                    nameof(ConfigurationSchemaHttp.SetAnnotationSchema) => await http.SetAnnotationSchema(request, Org, Project, View, Key),
                    _ => await http.SetDescendantTypeSchema(request, Org, Project, View, Key, AnnotationTypeCodes.Execution),
                };
                return (result, Entries(logger.Entries));
            }

            default:
            {
                var logger = new CapturingLogger<TemplateHttp>();
                var http = new TemplateHttp(Untouched<IConfigurationTemplateService>(), access, logger);
                var result = route == nameof(TemplateHttp.SetTemplate)
                    ? await http.SetTemplate(TestRequests.User("PUT", NotJson), Org, Project, View, AnnotationTypeCodes.Execution)
                    : await http.PatchTemplate(TestRequests.User("PATCH", NotJson), Org, Project, View, AnnotationTypeCodes.Execution);
                return (result, Entries(logger.Entries));
            }
        }
    }

    private static IReadOnlyCollection<CapturingLogger<object>.Entry> Entries<T>(IEnumerable<CapturingLogger<T>.Entry> entries)
    {
        return entries.Select(entry => new CapturingLogger<object>.Entry(entry.Level, entry.Message, entry.Values, entry.Exception)).ToList();
    }

    private static T Untouched<T>()
        where T : class
    {
        return DispatchProxy.Create<T, ConfigurationPatchHttpTests.UntouchedProxy>();
    }
}
