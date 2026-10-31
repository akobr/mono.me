using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace _42.Platform.Storyteller.Binding.Object.UnitTests;

/// <summary>
/// <see cref="EngineMessages"/> forwards every JSON-e message except the ones known to quote an evaluated value.
/// This pins the set of interpolated messages of the vendored fork: a sync that adds one fails here, so it gets
/// reviewed (and redacted when it can quote a value) before it reaches the API's responses.
/// </summary>
public class JsonEMessageSitesTests
{
    private static readonly string[] Known =
    [
        "ContextAccessor.cs: throw new TemplateException($\"{name} is not a valid accessor\");",
        "EvaluationContext.cs: throw new InterpreterException($\"unknown context value {identifier}\");",
        "FunctionExpressionNode.cs: throw new InterpreterException($\"unknown context value \\\"{FunctionExpression}\\\"\");",
        "FunctionExpressionNode.cs: throw new InterpreterException($\"{functionNode} is not callable\");",
        "DefinedFunction.cs: throw new InterpreterException($\"'{str}' is not a valid context accessor\");",
        "ValueAccessor.cs: throw new InterpreterException($\"object has no property \\\"{segment}\\\"\");",
        "ValueAccessor.cs: throw new InterpreterException($\"unknown context value \\\"{segment}\\\"\");",
        "JsonE.cs: throw new TemplateException($\"interpolation of '{exprText.ToString()}' produced an array or object\");",
        "JsonNodeExtensions.cs: throw new TemplateException($\"{(location != null ? location + \" value\" : \"context\")} must be an object\");",
        "JsonNodeExtensions.cs: throw new TemplateException($\"top level keys of {location ?? \"context\"} must follow /[a-zA-Z_][a-zA-Z0-9_]*/\");",
    ];

    [Fact]
    public void InterpolatedMessagesOfTheFork_AreTheReviewedOnes()
    {
        var sources = FindForkSources();

        var sites = Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadLines(file)
                .Select(line => line.Trim())
                .Where(line => Regex.IsMatch(line, @"throw new \w*Exception\(\$"""))
                .Select(line => $"{Path.GetFileName(file)}: {line}"))
            .ToList();

        sites.Should().BeEquivalentTo(Known);
    }

    private static string FindForkSources()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            foreach (var candidate in new[]
            {
                Path.Combine(directory.FullName, "Binding.JsonE", "src"),
                Path.Combine(directory.FullName, "src", "Platform", "Storyteller", "Binding.JsonE", "src"),
            })
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new DirectoryNotFoundException("The sources of Binding.JsonE were not found above the test output.");
    }
}
