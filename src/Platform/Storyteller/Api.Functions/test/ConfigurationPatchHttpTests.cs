using System.Reflection;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Configuring;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The patch route leaves the "nothing stored" decision to the service, inside its retry loop, and answers its refusal
// with 404.
public class ConfigurationPatchHttpTests
{
    private const string Org = "acme";
    private const string Project = "billing";
    private const string View = "default";
    private const string Key = "rst.payments";

    [Fact]
    public async Task PatchConfiguration_WithNothingStored_AnswersNotFoundWithTheMessage()
    {
        var configurations = DispatchProxy.Create<IConfigurationService, NothingStoredProxy>();
        var proxy = (NothingStoredProxy)(object)configurations;
        var http = new ConfigurationHttp(
            configurations,
            DispatchProxy.Create<IAnnotationService, UntouchedProxy>(),
            new RoleAccess(AccountRole.Contributor),
            new FixedKeyValuePolicies(requireMtls: false),
            NullLogger<ConfigurationHttp>.Instance);

        var result = await http.PatchConfiguration(
            TestRequests.User("PATCH", """[{ "op": "add", "path": "/a", "value": 1 }]"""),
            Org,
            Project,
            View,
            Key);

        var notFound = result.ShouldBeOfType<NotFoundObjectResult>();
        var error = notFound.Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(new ConfigurationNotFoundException(Key).Message);
        proxy.Keys.ShouldHaveSingleItem().ToString().ShouldBe(FullKey.Create(AnnotationKey.Parse(Key), Org, Project, View).ToString());
        proxy.Operations.ShouldHaveSingleItem().Count.ShouldBe(1);
    }

    // The service of a configuration without a stored version: every patch is refused.
    public class NothingStoredProxy : DispatchProxy
    {
        public List<FullKey> Keys { get; } = [];

        public List<JArray> Operations { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IConfigurationService.PatchConfigurationAsync))
            {
                throw new InvalidOperationException($"{targetMethod?.Name} must not be called.");
            }

            var key = (FullKey)args![0]!;
            Keys.Add(key);
            Operations.Add((JArray)args[1]!);
            return Task.FromException<Configuration>(new ConfigurationNotFoundException(key.Annotation));
        }
    }

    public class UntouchedProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            throw new InvalidOperationException($"{targetMethod?.Name} must not be called.");
        }
    }
}
