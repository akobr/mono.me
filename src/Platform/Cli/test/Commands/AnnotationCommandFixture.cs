using System;
using System.Collections.Generic;
using System.Linq;
using _42.Platform.Cli.Commands;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Moq;

namespace _42.Platform.Cli.UnitTests.Commands;

// The doubles the annotation commands share: a recording console, the annotations API and the default target.
internal sealed class AnnotationCommandFixture
{
    public const string Organization = "org1";
    public const string Project = "proj1";
    public const string View = "default";

    public AnnotationCommandFixture()
    {
        Context.SetupGet(context => context.OrganizationName).Returns(Organization);
        Context.SetupGet(context => context.ProjectName).Returns(Project);
        Context.SetupGet(context => context.ViewName).Returns(View);
        Console.Mock.SetupGet(console => console.Console).Returns(InnerConsole.Object);
    }

    public RecordingConsole Console { get; } = new();

    public Mock<IConsole> InnerConsole { get; } = new();

    public Mock<ICommandContext> Context { get; } = new();

    public Mock<IAnnotationsApiClient> Api { get; } = new(MockBehavior.Strict);

    public static ApiException Error(int statusCode, string? errorCode = null, string? message = null)
    {
        var headers = new Dictionary<string, IEnumerable<string>>();

        return errorCode is null && message is null
            ? new ApiException($"HTTP {statusCode}", statusCode, string.Empty, headers, null)
            : new ApiException<ErrorResponse>(
                $"HTTP {statusCode}",
                statusCode,
                string.Empty,
                headers,
                new ErrorResponse { ErrorCode = errorCode, Message = message },
                null);
    }

    // The 403 of the API for a machine that was not created with the management scope.
    public static ApiException MissingManagementScope(string scope)
    {
        return Error(403, "AccessDenied", MissingManagementScopeMessage(scope));
    }

    public static string MissingManagementScopeMessage(string scope)
    {
        return $"This operation needs the management scope {scope}, which the machine was not created with.";
    }

    public static AnnotationDeletion Deletion(string rootKey, int? descendantCount = null, bool isRoot = true)
    {
        return new AnnotationDeletion
        {
            DeletionId = "d1",
            RootKey = rootKey,
            DeletedAt = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero),
            DeletedBy = new AuditActor { Kind = AuditActorKind.Account, Id = "user_1", Name = "Ales Kobr" },
            IsRoot = isRoot,
            DescendantCount = descendantCount,
            PurgeAt = new DateTimeOffset(2027, 1, 9, 12, 0, 0, TimeSpan.Zero),
        };
    }

    public static Annotation Active(string key)
    {
        return new Annotation
        {
            ProjectName = Project,
            ViewName = View,
            AnnotationKey = key,
            Name = key.Split('.').Last(),
        };
    }

    public static Annotation Deleted(string key, AnnotationDeletion? deletion = null)
    {
        var annotation = Active(key);
        annotation.Deletion = deletion ?? Deletion(key);
        return annotation;
    }

    public string Line(string start)
    {
        return Console.Lines.Single(line => line.TrimStart().StartsWith(start, StringComparison.Ordinal));
    }
}
