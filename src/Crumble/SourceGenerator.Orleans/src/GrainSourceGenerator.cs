using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace _42.Crumble.SourceGenerator;

[Generator(LanguageNames.CSharp)]
internal sealed class GrainSourceGenerator : IIncrementalGenerator
{
    private static readonly SymbolDisplayFormat FullyQualifiedWithoutGlobalFormat = new SymbolDisplayFormat(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var crumbGrain =
            context.SyntaxProvider.CreateSyntaxProvider(
                CrumbMethodPredicate,
                CrumbMethodTransform);

        context.RegisterSourceOutput(crumbGrain, GenerateGrain);

        var allGrains = crumbGrain.Collect();
        context.RegisterSourceOutput(allGrains, GenerateGrainExecutors);
        context.RegisterSourceOutput(allGrains, GenerateGrainsRegistration);
        context.RegisterSourceOutput(allGrains, GenerateActionsRegistration);
        context.RegisterSourceOutput(allGrains, GenerateTypesRegistration);
        context.RegisterSourceOutput(allGrains, GenerateEntryPoint);
    }

    private static bool CrumbMethodPredicate(SyntaxNode n, CancellationToken _)
    {
        return n is MethodDeclarationSyntax { AttributeLists.Count: > 0 };
    }

    private static CrumbGrainGenerateModel CrumbMethodTransform(GeneratorSyntaxContext c, CancellationToken t)
    {
        Debug.Assert(c.Node is MethodDeclarationSyntax);

        var method = (MethodDeclarationSyntax)c.Node;
        var attribute = method.AttributeLists.SelectMany(list => list.Attributes)
            .FirstOrDefault(att => att.Name.ToString().Contains("Crumb"));

        if (attribute is null)
        {
            return default;
        }

        var methodSymbol = c.SemanticModel.GetDeclaredSymbol(method);

        if (methodSymbol is null)
        {
            return default;
        }

        var methodNameSuffix = methodSymbol.TypeParameters.IsEmpty
            ? string.Empty
            : $"~{methodSymbol.TypeParameters.Length}";

        var methodArguments = methodSymbol.Parameters.IsEmpty
            ? string.Empty
            : string.Join(",", methodSymbol.Parameters.Select(p => p.Type.ToDisplayString(FullyQualifiedWithoutGlobalFormat)));

        var classSymbol = methodSymbol.ContainingType;
        var namespaceSymbol = classSymbol.ContainingNamespace;
        var namespaceFullName = namespaceSymbol.ToDisplayString(FullyQualifiedWithoutGlobalFormat);

        var keyArg = (attribute.ArgumentList?.Arguments ?? []).FirstOrDefault(arg
            => (arg.NameEquals?.Name.ToString() ?? string.Empty) == nameof(CrumbAttribute.Key));
        var crumbKey = keyArg?.Expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } keyLiteral
            ? keyLiteral.Token.ValueText
            : null;

        if (string.IsNullOrWhiteSpace(crumbKey))
        {
            var keyFromMethod = $"{classSymbol.ToDisplayString(FullyQualifiedWithoutGlobalFormat)}.{methodSymbol.Name}{methodNameSuffix}({methodArguments})";
            var hashOfKey = MurmurHash3.Hash32(Encoding.UTF8.GetBytes(keyFromMethod), 4242424243U);
            crumbKey = $"{classSymbol.Name}.{methodSymbol.Name}.{hashOfKey:x8}";
        }

        //var crumbKey = $"{classSymbol.ToDisplayString(FullyQualifiedWithoutGlobalFormat)}.{methodSymbol.Name}{methodNameSuffix}({methodArguments})";
        var crumbName = classSymbol.Name + methodSymbol.Name;
        var isSingleton = (attribute.ArgumentList?.Arguments ?? []).Any(arg
            => (arg.NameEquals?.Name.ToString() ?? string.Empty) == nameof(CrumbAttribute.IsSingleAndSynchronized)
               && arg.Expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.TrueLiteralExpression });
        var hasOutput = !methodSymbol.ReturnsVoid;
        var outputType = hasOutput ? methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : "void";
        var isAsync = false;
        var hasInput = methodSymbol.Parameters.Length > 0; // TODO: [P2] params by DI directly in crumb method
        var inputType = hasInput ? methodSymbol.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : "void";

        // unwrapped output if Task or ValueTask is used
        if (methodSymbol.ReturnType.Name is nameof(Task) or nameof(ValueTask))
        {
            var taskType = methodSymbol.ReturnType as INamedTypeSymbol;
            isAsync = true;

            if (taskType.TypeArguments.Length != 1)
            {
                hasOutput = false;
                outputType = nameof(Task);
            }
            else
            {
                outputType = taskType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }

        var actions = method.AttributeLists.SelectMany(list => list.Attributes)
            .Where(att => att.Name.ToString().Contains("Action"))
            .Select(BuildAction)
            .ToArray();

        return new CrumbGrainGenerateModel
        {
            CrumbKey = crumbKey,
            CrumbName = crumbName,
            Method = methodSymbol,
            Class = classSymbol,
            Namespace = namespaceSymbol,
            NamespaceFullName = namespaceFullName,
            IsSingleAndSynchronized = isSingleton,
            HasInput = hasInput,
            InputTypeName = inputType,
            HasOutput = hasOutput,
            OutputTypeName = outputType,
            IsAsync = isAsync,
            ParameterCount = methodSymbol.Parameters.Length,
            Actions = actions,
        };
    }

    private static ActionModel BuildAction(AttributeSyntax syntax)
    {
        switch (syntax.Name.ToString())
        {
            case "TimeAction":
            {
                var cronArg = (syntax.ArgumentList?.Arguments ?? []).FirstOrDefault(arg
                    => arg.NameEquals is null);
                var cron = cronArg?.Expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } volumeLiteral
                    ? volumeLiteral.Token.ValueText
                    : "0 * * * *";
                var timeZoneArg = (syntax.ArgumentList?.Arguments ?? []).FirstOrDefault(arg
                    => (arg.NameEquals?.Name.ToString() ?? string.Empty) == nameof(TimeActionAttribute.TimeZone));
                var timeZone = timeZoneArg?.Expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } pathLiteral
                    ? pathLiteral.Token.ValueText
                    : null;

                return new ActionModel
                {
                    Type = ActionType.Time,
                    Cron = cron,
                    TimeZone = timeZone,
                };
            }

            case "MessageAction":
            {
                var queueKeyArg = (syntax.ArgumentList?.Arguments ?? []).FirstOrDefault(arg
                    => (arg.NameEquals?.Name.ToString() ?? string.Empty) == nameof(MessageActionAttribute.QueueKey));
                var queueKey = queueKeyArg?.Expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } queueLiteral
                    ? queueLiteral.Token.ValueText
                    : null;

                return new ActionModel
                {
                    Type = ActionType.Message,
                    ContextKey = queueKey,
                };
            }

            case "VolumeAction":
            {
                var volumeKeyArg = (syntax.ArgumentList?.Arguments ?? []).FirstOrDefault(arg
                    => (arg.NameEquals?.Name.ToString() ?? string.Empty) == nameof(VolumeActionAttribute.VolumeKey));
                var volumeKey = volumeKeyArg?.Expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } volumeLiteral
                    ? volumeLiteral.Token.ValueText
                    : null;
                var pathFilterArg = (syntax.ArgumentList?.Arguments ?? []).FirstOrDefault(arg
                    => (arg.NameEquals?.Name.ToString() ?? string.Empty) == nameof(VolumeActionAttribute.PathFilter));
                var pathFilter = pathFilterArg?.Expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } pathLiteral
                    ? pathLiteral.Token.ValueText
                    : null;

                return new ActionModel
                {
                    Type = ActionType.Volume,
                    ContextKey = volumeKey,
                    Filter = pathFilter,
                };
            }

            default:
                throw new InvalidOperationException($"Unknown action attribute: {syntax.Name}");
        }
    }

    private void GenerateEntryPoint(SourceProductionContext context, ImmutableArray<CrumbGrainGenerateModel> all)
    {
        if (all.IsDefaultOrEmpty)
        {
            return;
        }

        using StringWriter writer = new(CultureInfo.InvariantCulture);
        using IndentedTextWriter source = new(writer);
        var firstGrain = all.First(); // TODO: [P1] this needs to be more robust
        var namespaceFull = firstGrain.NamespaceFullName;
        var namespaceHash = MurmurHash3.Hash32(Encoding.UTF8.GetBytes(namespaceFull), 4242424243U);
        var classNameSuffix = $"{namespaceHash:x8}";

        // language=c#
        source.WriteLine($$"""
                           // <auto-generated/>
                           // Do not edit this file manually. Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.
                           // Generated by Crumble.SourceGenerator version 0.9
                           // Generated on {{DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture)}} UTC
                           #nullable enable

                           using global::Microsoft.Extensions.DependencyInjection;

                           namespace {{namespaceFull}};

                           [global::System.CodeDom.Compiler.GeneratedCode("Crumble.SourceGenerator", "0.9")]

                           public static class CrumblesEntryPoint_{{classNameSuffix}}
                           {
                               public static IServiceCollection AddCrumbs(this IServiceCollection @this)
                               {
                                   @this.RegisterExecutors();
                                   @this.Configure<CrumbToGrainRegistryOptions>(config => config.RegisterCrumbs());
                                   @this.Configure<ActionRegistryOptions>(config => config.RegisterActions());
                                   return @this;
                               }
                           }
                           """);

        var code = writer.ToString();
        context.AddSource($"CrumblesEntryPoint.g.cs", code);
    }

    private void GenerateTypesRegistration(SourceProductionContext context, ImmutableArray<CrumbGrainGenerateModel> all)
    {
        if (all.IsDefaultOrEmpty)
        {
            return;
        }

        using StringWriter writer = new(CultureInfo.InvariantCulture);
        using IndentedTextWriter source = new(writer);
        var firstGrain = all.First(); // TODO: [P1] this needs to be more robust
        var namespaceFull = firstGrain.NamespaceFullName;
        var namespaceHash = MurmurHash3.Hash32(Encoding.UTF8.GetBytes(namespaceFull), 4242424243U);
        var classNameSuffix = $"{namespaceHash:x8}";
        var executorsSet = new HashSet<string>();

        // language=c#
        source.WriteLine($$"""
                           // <auto-generated/>
                           // Do not edit this file manually. Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.
                           // Generated by Crumble.SourceGenerator version 0.9
                           // Generated on {{DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture)}} UTC
                           #nullable enable

                           using global::System;
                           using global::System.Threading.Tasks;
                           using global::_42.Crumble;
                           using global::Microsoft.Extensions.DependencyInjection;
                           using global::Microsoft.Extensions.DependencyInjection.Extensions;

                           namespace {{namespaceFull}};

                           [global::System.CodeDom.Compiler.GeneratedCode("Crumble.SourceGenerator", "0.9")]

                           public static class ExecutorsTypeRegistryExtensions_{{classNameSuffix}}
                           {
                               public static IServiceCollection RegisterExecutors(this IServiceCollection @this)
                               {
                           """);

        foreach (var grain in all)
        {
            var executorServiceTypeName = $"I{grain.Class.Name}Executor";

            if (!executorsSet.Add(executorServiceTypeName))
            {
                continue;
            }

            // language=c#
            source.WriteLine($$"""
                                       @this.AddTransient<{{executorServiceTypeName}}, {{grain.Class.Name}}Executor>();
                               """);
        }

        // language=c#
        source.WriteLine($$"""
                                   return @this;
                               }
                           }
                           """);

        var code = writer.ToString();
        context.AddSource($"ExecutorTypeRegistrations.g.cs", code);
    }

    private void GenerateActionsRegistration(SourceProductionContext context, ImmutableArray<CrumbGrainGenerateModel> all)
    {
        if (all.IsDefaultOrEmpty)
        {
            return;
        }

        using StringWriter writer = new(CultureInfo.InvariantCulture);
        using IndentedTextWriter source = new(writer);
        var firstGrain = all.First(); // TODO: [P1] this needs to be more robust
        var namespaceFull = firstGrain.NamespaceFullName;
        var namespaceHash = MurmurHash3.Hash32(Encoding.UTF8.GetBytes(namespaceFull), 4242424243U);
        var classNameSuffix = $"{namespaceHash:x8}";

        // language=c#
        source.WriteLine($$"""
                           // <auto-generated/>
                           // Do not edit this file manually. Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.
                           // Generated by Crumble.SourceGenerator version 0.9
                           // Generated on {{DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture)}} UTC
                           #nullable enable

                           using global::System;
                           using global::System.Threading.Tasks;
                           using global::_42.Crumble;
                           using global::Orleans;
                           using global::Microsoft.Extensions.DependencyInjection;

                           namespace {{namespaceFull}};

                           [global::System.CodeDom.Compiler.GeneratedCode("Crumble.SourceGenerator", "0.9")]

                           public static class ActionRegistryExtensions_{{classNameSuffix}}
                           {
                               public static IActionRegister RegisterActions(this IActionRegister @this)
                               {
                           """);

        foreach (var grain in all)
        {
            if (grain.Actions.Count < 1)
            {
                continue;
            }

            var executorType = $"I{grain.Class.Name}Executor";
            var methodName = grain.Method.Name;

            foreach (var action in grain.Actions)
            {
                var actionType = $"{action.Type:G}ActionAttribute";
                var inputType = string.Empty;
                var actionInit = string.Empty;
                var inputParam = "input";

                switch (action.Type)
                {
                    case ActionType.Time:
                    {
                        inputType = "DateTime";
                        var timeZoneArg = action.TimeZone is not null ? $"\"{action.TimeZone}\"" : "null";
                        actionInit = $"{actionType}(\"{action.Cron}\") {{ {nameof(TimeActionAttribute.TimeZone)} = {timeZoneArg} }}";
                        break;
                    }

                    case ActionType.Message:
                    {
                        inputType = "global::_42.Crumble.MessageModel";
                        var queueKeyArg = action.ContextKey is not null ? $"\"{action.ContextKey}\"" : "null";
                        actionInit = $"{actionType}() {{ {nameof(MessageActionAttribute.QueueKey)} = {queueKeyArg} }}";

                        if (grain.InputTypeName.Equals(nameof(String), StringComparison.OrdinalIgnoreCase))
                        {
                            inputParam = "input.MessageText";
                        }

                        break;
                    }

                    case ActionType.Volume:
                    {
                        inputType = "string";
                        var volumeArg = action.ContextKey is not null ? $"\"{action.ContextKey}\"" : "null";
                        var pathFilterArg = action.Filter is not null ? $"\"{action.Filter}\"" : "null";
                        actionInit = $"{actionType}() {{ {nameof(VolumeActionAttribute.VolumeKey)} = {volumeArg}, {nameof(VolumeActionAttribute.PathFilter)} = {pathFilterArg} }}";
                        break;
                    }

                    default:
                        continue;
                }

                // language=c#
                source.WriteLine($$"""
                                           @this.RegisterAction(new ActionModel<{{actionType}}, {{inputType}}>()
                                           {
                                               Action = new {{actionInit}},
                                               CrumbKey = "{{grain.CrumbKey}}",
                                               Executor = (services, input) =>
                                               {
                                                   var executor = services.GetRequiredService<{{executorType}}>();
                                                   return executor.{{methodName}}({{inputParam}});
                                               }
                                           });
                                   """);
            }
        }

        // language=c#
        source.WriteLine($$"""
                                   return @this;
                               }
                           }
                           """);

        var code = writer.ToString();
        context.AddSource($"ActionRegistrations.g.cs", code);
    }

    private void GenerateGrainsRegistration(SourceProductionContext context, ImmutableArray<CrumbGrainGenerateModel> all)
    {
        if (all.IsDefaultOrEmpty)
        {
            return;
        }

        using StringWriter writer = new(CultureInfo.InvariantCulture);
        using IndentedTextWriter source = new(writer);
        var firstGrain = all.First(); // TODO: [P1] this needs to be more robust
        var namespaceFull = firstGrain.NamespaceFullName;
        var namespaceHash = MurmurHash3.Hash32(Encoding.UTF8.GetBytes(namespaceFull), 4242424243U);
        var classNameSuffix = $"{namespaceHash:x8}";

        // language=c#
        source.WriteLine($$"""
                           // <auto-generated/>
                           // Do not edit this file manually. Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.
                           // Generated by Crumble.SourceGenerator version 0.9
                           // Generated on {{DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture)}} UTC
                           #nullable enable

                           using global::System;
                           using global::System.Threading.Tasks;
                           using global::_42.Crumble;
                           using global::Orleans;

                           namespace {{namespaceFull}};

                           [global::System.CodeDom.Compiler.GeneratedCode("Crumble.SourceGenerator", "0.9")]

                           public static class CrumbToGrainRegisterExtensions_{{classNameSuffix}}
                           {
                               public static ICrumbToGrainRegister RegisterCrumbs(this ICrumbToGrainRegister @this)
                               {
                           """);

        foreach (var grain in all)
        {
            // language=c#
            source.WriteLine($$"""
                                       @this.RegisterCrumb("{{grain.CrumbKey}}", typeof({{grain.NamespaceFullName}}.I{{grain.CrumbName}}Grain));
                               """);
        }

        // language=c#
        source.WriteLine($$"""
                                   return @this;
                               }
                           }
                           """);

        var code = writer.ToString();
        context.AddSource($"CrumbToGrainRegistrations.g.cs", code);
    }

    private void GenerateGrainExecutors(SourceProductionContext context, ImmutableArray<CrumbGrainGenerateModel> all)
    {
        if (all.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var classGroup in all
                     .OrderBy(m => m.CrumbKey)
                     .GroupBy(m => m.Class.Name))
        {
            using StringWriter writer = new(CultureInfo.InvariantCulture);
            using IndentedTextWriter source = new(writer);
            var firstGrain = classGroup.First();

            // language=c#
            source.WriteLine($$"""
                               // <auto-generated/>
                               // Do not edit this file manually. Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.
                               // Generated by Crumble.SourceGenerator version 0.9
                               // Generated on {{DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture)}} UTC
                               #nullable enable

                               using global::System;
                               using global::System.Threading.Tasks;
                               using global::_42.Crumble;
                               using global::Orleans;

                               namespace {{firstGrain.NamespaceFullName}};

                               [global::System.CodeDom.Compiler.GeneratedCode("Crumble.SourceGenerator", "0.9")]

                               public interface I{{firstGrain.Class.Name}}Executor
                               {
                               """);

            foreach (var crumb in classGroup)
            {
                // language=c#
                source.WriteLine($$"""
                                       Task{{(crumb.HasOutput ? $"<{crumb.OutputTypeName}>" : string.Empty)}} {{crumb.Method.Name}}({{(crumb.HasInput ? crumb.InputTypeName + " input" : string.Empty)}});
                                   """);
            }

            // language=c#
            source.WriteLine($$"""
                               }

                               public partial class {{firstGrain.Class.Name}}Executor(
                                   global::Orleans.IGrainFactory grainFactory,
                                   global::_42.Crumble.ICrumbExecutionContextProvider? contextProvider)
                                   : I{{firstGrain.Class.Name}}Executor
                               {
                                   private string ContextKey => contextProvider?.ContextKey ?? "default";

                               """);

            foreach (var crumb in classGroup)
            {
                // language=c#
                source.WriteLine($$"""
                                       public Task{{(crumb.HasOutput ? $"<{crumb.OutputTypeName}>" : string.Empty)}} {{crumb.Method.Name}}({{(crumb.HasInput ? crumb.InputTypeName + " input" : string.Empty)}})
                                       {
                                            var grain = grainFactory.GetGrain<I{{crumb.CrumbName}}Grain>(ContextKey);
                                            return grain.ExecuteCrumb({{(crumb.HasInput ? "input" : string.Empty)}});
                                       }

                                   """);
            }

            // language=c#
            source.WriteLine($$"""
                               }
                               """);

            var code = writer.ToString();
            context.AddSource($"{firstGrain.Class.Name}Executor.g.cs", code);
        }
    }

    private void GenerateGrain(SourceProductionContext context, CrumbGrainGenerateModel model)
    {
        if (model.Method.IsStatic)
        {
            // TODO: static method
            return;
        }

        var method = model.Method;
        var @class = model.Class;

        using StringWriter writer = new(CultureInfo.InvariantCulture);
        using IndentedTextWriter source = new(writer);

        // language=c#
        source.WriteLine($$"""
                           // <auto-generated/>
                           // Do not edit this file manually. Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.
                           // Generated by Crumble.SourceGenerator version 0.9
                           // Generated on {{DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture)}} UTC
                           #nullable enable

                           using global::System;
                           using global::System.Threading.Tasks;
                           using global::_42.Crumble;
                           using global::Orleans;

                           namespace {{model.NamespaceFullName}};

                           [global::System.CodeDom.Compiler.GeneratedCode("Crumble.SourceGenerator", "0.9")]

                           """);

        var @interface = "ICrumbGrain";
        var executeMethodDefinition = "Task ExecuteCrumb()";
        var inputValue = model.HasInput ? "input" : "null";

        if (model.HasOutput)
        {
            @interface = "ICrumbGrainWithOutput<" + model.OutputTypeName;
            executeMethodDefinition = "Task<" + model.OutputTypeName + "> ExecuteCrumb(";

            if (model.HasInput)
            {
                @interface += ", " + model.InputTypeName + ">";
                executeMethodDefinition += model.InputTypeName + " input)";
            }
            else
            {
                @interface += ">";
                executeMethodDefinition += ")";
            }
        }
        else if(model.HasInput)
        {
            @interface += "<" + model.InputTypeName + ">";
            executeMethodDefinition = "Task ExecuteCrumb(" + model.InputTypeName + " input)";
        }

        // language=c#
        source.WriteLine($$"""
                           public interface I{{model.CrumbName}}Grain : {{@interface}}
                           {
                               // no member
                           }

                           """);

        var lambdaPrefix = model is { HasOutput: true, IsAsync: true } ? "async " : "";

        if (!model.IsSingleAndSynchronized)
        {
            // language=c#
            source.WriteLine($$"""
                               [global::Orleans.Concurrency.StatelessWorker]
                               """);
        }

        // language=c#
        source.WriteLine($$"""
                           public class {{model.CrumbName}}Grain(ICrumbExecutorFactory executorFactory) : Grain, I{{model.CrumbName}}Grain
                           {
                               public async {{executeMethodDefinition}}
                               {
                                   await using var executor = executorFactory.CreateExecutor();
                                   var executionContext = executor.PrepareExecutionContext(this);
                                   var instance = executor.CreateCrumbInstance<{{@class.Name}}>();
                           """);

        if (model.HasOutput)
        {
            // language=c#
            source.WriteLine($$"""
                                       {{model.OutputTypeName}} output = default;
                               """);
        }

        // language=c#
        source.WriteLine($$"""

                                   var context = new CrumbInnerExecutionContext()
                                   {
                                       CrumbKey = "{{model.CrumbKey}}",
                                       Instance = instance,
                                       Input = {{inputValue}},
                                       ExecutionContext = executionContext,
                                       Settings = new CrumbExecutionSetting(),
                                   };

                                   await executor.ExecuteCrumbWithMiddlewares(
                                       context,
                                       {{lambdaPrefix}}() =>
                                       {
                           """);

        var inputParameter = model.HasInput ? "input" : string.Empty;

        if(model.HasOutput)
        {
            if (!model.IsAsync)
            {
                // language=c#
                source.WriteLine($$"""
                                                   output = instance.{{method.Name}}({{inputParameter}});
                                                   context.Output = output;
                                                   return Task.CompletedTask;
                                   """);
            }
            else
            {
                // language=c#
                source.WriteLine($$"""
                                                   output = await instance.{{method.Name}}({{inputParameter}});
                                                   context.Output = output;
                                   """);
            }

            var grainInterfaceGeneric = model.HasInput ? "<" + model.InputTypeName + ">" : string.Empty;
            var inputParameterDefinition = model.HasInput ? model.InputTypeName + " input" : string.Empty;
            // language=c#
            source.WriteLine($$"""
                                           });

                                       return output;
                                   }

                                   Task ICrumbGrain{{grainInterfaceGeneric}}.ExecuteCrumb({{inputParameterDefinition}})
                                   {
                                       return ExecuteCrumb({{inputParameter}});
                                   }
                               }
                               """);
        }
        else
        {
            if (model.IsAsync)
            {
                // language=c#
                source.WriteLine($$"""
                                                   return instance.{{method.Name}}({{inputParameter}});
                                   """);
            }
            else
            {
                // language=c#
                source.WriteLine($$"""
                                                   instance.{{method.Name}}({{inputParameter}});
                                                   return Task.CompletedTask;
                                   """);
            }

            // language=c#
            source.WriteLine($$"""
                                           });
                                   }
                               }
                               """);
        }

        var code = writer.ToString();
        context.AddSource($"{model.CrumbName}Grain.g.cs", code);
    }
}
