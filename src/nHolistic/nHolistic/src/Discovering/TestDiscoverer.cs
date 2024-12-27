using System.Globalization;
using System.Reflection;
using MediatR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.nHolistic;

public class TestDiscoverer(IPublisher publisher) : ITestDiscoverer
{
    public void DiscoverTests(Assembly assembly, string sourceName)
    {
        foreach (var type in assembly.GetTypes())
        {
            ProcessType(type, assembly, sourceName);
        }
    }

    private void ProcessType(Type type, Assembly assembly, string sourceName)
    {
        if (!type.IsClass || type.IsAbstract)
        {
            return;
        }

        // TODO: [P1] Move this to separated library, by default it should not to be supported (same for Order attribute)
        var classAttribute = type.GetCustomAttribute<TestAttribute>();

        if (classAttribute is not null)
        {
            BuildTestCasesForClass(type, classAttribute, assembly, sourceName);
            return;
        }

        BuildTestCasesForMethods(type, assembly, sourceName);
    }

    private void BuildTestCasesForClass(Type type, TestAttribute attribute, Assembly assembly, string sourceName)
    {
        var stepMethods = type
            .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .ToList();

        if (!stepMethods.Any())
        {
            // no steps in the class (no public methods)
            return;
        }

        stepMethods.Sort((a, b) =>
        {
            a.TryGetOrder(out var aOrder);
            b.TryGetOrder(out var bOrder);

            return aOrder != bOrder
                ? aOrder.CompareTo(bOrder)
                : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        var typeFullName = type.FullName ?? type.Name;
        var firstMethod = stepMethods[0];
        var resourceName = $"{type.Namespace}.{type.Name}";
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {

            var classTestCase = BuildTestCase(
                $"{typeFullName}.{firstMethod.Name}",
                $"{type.Name} [{stepMethods.Count} steps]",
                attribute,
                null,
                type.Namespace ?? "Global",
                sourceName,
                typeFullName);

            publisher.Publish(new TestCaseDiscoveredNotification { TestCase = classTestCase });
            return;
        }

        using var reader = new StreamReader(stream);
        using var jsonReader = new JsonTextReader(reader);
        var modelRoot = JToken.ReadFrom(jsonReader);
        var hasModel = false;

        foreach (var model in GetModels(modelRoot))
        {
            hasModel = true;
            var classTestCase = BuildTestCase(
                $"{typeFullName}.{firstMethod.Name}(model: {model.Name})",
                $"{model.Name} ({stepMethods.Count} steps)",
                attribute,
                model.Model,
                type.Namespace ?? "Global",
                sourceName,
                typeFullName);

            publisher.Publish(new TestCaseDiscoveredNotification { TestCase = classTestCase });
        }

        if (!hasModel)
        {
            publisher.Publish(new LogNotification
            {
                Level = LogMessageLevel.Error,
                Message = $"Class test '{type.FullName ?? type.Name}' has invalid model. It can be an object or an array of objects.",
            });
        }
    }

    private void BuildTestCasesForMethods(Type type, Assembly assembly, string sourceName)
    {
        var allMethods = type
            .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<TestAttribute>(true)))
            .Where(pair => pair.Attribute is not null)
            .ToArray();

        if (allMethods.Length < 1)
        {
            return;
        }

        var resourceName = $"{type.Namespace}.{type.Name}";
        var typeFullName = type.FullName ?? type.Name;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        JToken modelRoot = new JObject();
        var hasValidModel = stream is not null;

        if (hasValidModel)
        {
            using var reader = new StreamReader(stream!);
            using var jsonReader = new JsonTextReader(reader);
            modelRoot = JToken.ReadFrom(jsonReader);
            hasValidModel = (modelRoot is JObject or JValue) | (allMethods.Length == 1 && modelRoot.Type == JTokenType.Array);
        }

        foreach (var pair in allMethods)
        {
            var modelParameters = pair.Method
                .GetParameters()
                .Select(parameter => (Parameter: parameter, Attribute: parameter.GetCustomAttribute<InjectionAttribute>()))
                .Where(parPair => parPair.Attribute is null || parPair.Attribute.InjectionType == InjectionType.Model)
                .ToArray();

            if (modelParameters.Length > 0)
            {
                if (hasValidModel)
                {
                    var methodModelRoot = modelRoot[pair.Method.Name];

                    if (methodModelRoot is null && allMethods.Length == 1)
                    {
                        methodModelRoot = modelRoot;
                    }

                    foreach (var model in GetModels(methodModelRoot))
                    {
                        var methodTestCase = BuildTestCase(
                            $"{typeFullName}.{pair.Method.Name}(model: {model.Name})",
                            $"{pair.Method.Name}(model: {model.Name})",
                            pair.Attribute!,
                            model.Model,
                            type.FullName ?? type.Namespace ?? type.Name,
                            sourceName,
                            typeFullName,
                            pair.Method.Name);
                        publisher.Publish(new TestCaseDiscoveredNotification { TestCase = methodTestCase });
                    }
                }
                else
                {
                    publisher.Publish(new LogNotification
                    {
                        Level = LogMessageLevel.Error,
                        Message = $"Method test '{type.FullName ?? type.Name}.{pair.Method.Name}' has invalid model. It can be an object, value, or an array.",
                    });
                }
            }
            else
            {
                var methodTestCase = BuildTestCase(
                    $"{typeFullName}.{pair.Method.Name}",
                    pair.Method.Name,
                    pair.Attribute!,
                    null,
                    type.FullName ?? type.Namespace ?? type.Name,
                    sourceName,
                    typeFullName,
                    pair.Method.Name);
                publisher.Publish(new TestCaseDiscoveredNotification { TestCase = methodTestCase });
            }
        }
    }

    private IEnumerable<(string Name, JToken Model)> GetModels(JToken? modelRoot)
    {
        switch (modelRoot)
        {
            case JValue:
            {
                yield return ("Model", modelRoot);
                break;
            }

            case JObject:
            {
                var modelName = modelRoot.Value<string>("$name");

                if (string.IsNullOrWhiteSpace(modelName))
                {
                    modelName = "Model";
                }

                yield return (modelName, modelRoot);
                break;
            }

            case JArray modelArray:
            {
                for (var i = 0; i < modelArray.Count; i++)
                {
                    var modelItem = modelArray[i];
                    var modelItemName = $"Models[{i}]";

                    if (modelItem is JValue)
                    {
                        yield return (modelItemName, modelItem);
                        continue;
                    }

                    if (modelItem is JObject modelObject)
                    {
                        var modelCustomName = modelObject.Value<string>("$name");
                        if (!string.IsNullOrWhiteSpace(modelCustomName))
                        {
                            modelItemName = modelCustomName;
                        }

                        yield return (modelItemName, modelItem);
                    }
                }

                break;
            }
        }
    }

    private TestCase BuildTestCase(
        string fullQualifiedName,
        string name,
        TestAttribute testAttribute,
        JToken? model,
        string labelPath,
        string source,
        string typeFullName,
        string? methodName = null)
    {
        var testCase = new TestCase
        {
            FullyQualifiedName = fullQualifiedName,
            TypeFullyQualifiedName = typeFullName,
            MethodName = methodName,
            DisplayName = name,
            Source = source,
            Model = model,
        };

        testCase.Properties.Add(new TestCaseProperty { Name = nameof(TestCase.TypeFullyQualifiedName), Value = testCase.TypeFullyQualifiedName });

        if (testCase.MethodName is not null)
        {
            testCase.Properties.Add(new TestCaseProperty { Name = nameof(TestCase.MethodName), Value = testCase.MethodName });
        }

        testCase.Dependencies.UnionWith(testAttribute.Dependencies ?? []);

        if (model is not null)
        {
            testCase.Properties.Add(new TestCaseProperty { Name = nameof(TestCase.Model), Value = model.ToString(Formatting.None) });
        }

        if (testAttribute.Priority != 0)
        {
            testCase.Traits.Add(new TestCaseProperty
            {
                Name = nameof(TestAttribute.Priority),
                Value = testAttribute.Priority.ToString(CultureInfo.InvariantCulture),
            });
        }

        if (testAttribute.Labels is not null && testAttribute.Labels.Length > 0)
        {
            testCase.Labels.UnionWith(testAttribute.Labels);
            testCase.Traits.AddRange(testAttribute.Labels
                .Select(label => new TestCaseProperty { Name = "Label", Value = label }));
        }

        if (!string.IsNullOrEmpty(labelPath))
        {
            var segments = labelPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
            testCase.Traits.AddRange(segments.Select(
                segment => new TestCaseProperty { Name = "Label", Value = segment }));
            testCase.Labels.UnionWith(segments);

            var labelCut = labelPath;
            var lastIndexOfDot = labelCut.Length;

            while (lastIndexOfDot > 0)
            {
                labelCut = labelCut[..lastIndexOfDot];
                lastIndexOfDot = labelCut.LastIndexOf('.');
                testCase.Traits.Add(new TestCaseProperty { Name = "Label", Value = labelCut });
                testCase.Labels.Add(labelCut);
            }
        }

        return testCase;
    }
}
