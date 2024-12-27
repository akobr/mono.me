using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.nHolistic;

public class TypeActivator(
    IServiceProvider services,
    Lazy<IFixtureProvider> fixturesLazy,
    IProxyFactory proxyFactory)
    : ITypeActivator
{
    private IFixtureProvider Fixtures => fixturesLazy.Value;

    public object Activate(Type type, TestCase? testCase)
    {
        if (!type.IsClass || type.IsAbstract)
        {
            throw new InvalidOperationException($"Type '{type.FullName}' is not instantiable type.");
        }

        var constructors = type.GetConstructors();

        switch (constructors.Length)
        {
            case 0:
                throw new InvalidOperationException($"Type '{type.FullName}' does not have a public constructor.");

            case > 1:
                throw new InvalidOperationException($"Type '{type.FullName}' has more than one public constructor.");
        }

        var constructor = constructors[0];
        var arguments = ResolveParameters(constructor.GetParameters(), testCase);
        var instance = constructor.Invoke(arguments);
        return TryProxySteps(instance, type, arguments);
    }

    public object[] ResolveParameters(ParameterInfo[] parameters, TestCase? testCase)
    {
        var resultParameters = new object[parameters.Length];

        foreach (var parameter in parameters)
        {
            resultParameters[parameter.Position] = ResolveParameter(parameter, testCase);
        }

        return resultParameters;
    }

    public object ResolveParameter(ParameterInfo parameter, TestCase? testCase)
    {
        var fromAttributes = parameter.GetCustomAttributes<InjectionAttribute>().ToArray();

        if (fromAttributes.Length > 1)
        {
            throw new InvalidOperationException(
                $"The parameter '{parameter.Name}' has more than one injection attribute.");
        }

        if (fromAttributes.Length == 0)
        {
            return ResolveGenericParameter(parameter, testCase);
        }

        return fromAttributes[0].InjectionType switch
        {
            InjectionType.Container => ResolveParameterFromContainer(parameter, (FromContainerAttribute)fromAttributes[0]),
            InjectionType.Model => ResolveParameterFromModel(parameter, (FromModelAttribute)fromAttributes[0], testCase),
            InjectionType.Fixture => ResolveParameterFromFixture(parameter, (FromFixtureAttribute)fromAttributes[0], testCase),
            _ => throw new InvalidOperationException($"Parameter '{parameter.Name}' has unsupported injection attribute '{fromAttributes[0].InjectionType}'."),
        };
    }

    private object ResolveGenericParameter(ParameterInfo parameter, TestCase? testCase)
    {
        var isBasicType = parameter.ParameterType.IsValueType || parameter.ParameterType == typeof(string);

        if (isBasicType)
        {
            if (testCase?.Model is null)
            {
                throw new InvalidOperationException($"Parameter '{parameter.Name}' is a basic value type which can be used only with model source.");
            }

            var basicValue = testCase.Model.ToObject(parameter.ParameterType);

            if (basicValue is null)
            {
                throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from model, but the value could not be created.");
            }

            return basicValue;
        }

        var service = services.GetService(parameter.ParameterType);

        if (service is not null)
        {
            // TODO: [P1] it won't work for types without empty constructor!
            return TryProxySteps(service, parameter.ParameterType, null);
        }

        if (testCase is not null)
        {
            var fixtureObjects = Fixtures.GetFixtures(testCase.FullyQualifiedName);
            var fixture = fixtureObjects.FirstOrDefault(parameter.ParameterType.IsInstanceOfType);

            if (fixture is not null)
            {
                return fixture;
            }

            if (parameter.ParameterType.IsInterface
                && testCase.Model is not null)
            {
                var serializer = JsonSerializer.CreateDefault(); // TODO: [P2] use configurable serializer with null rules
                serializer.Converters.Add(new NewtonsoftProxyJsonConverter(proxyFactory, parameter.ParameterType));
                var modelInterface = testCase.Model.ToObject(parameter.ParameterType, serializer);

                if (modelInterface is null)
                {
                    throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from model, but the model object could not be created.");
                }

                return modelInterface;
            }
        }

        try
        {
            var activation = Activate(parameter.ParameterType, testCase);
            return activation;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Parameter '{parameter.Name}' of type '{parameter.ParameterType.FullName}' could not be resolved.",
                exception);
        }
    }

    private object ResolveParameterFromContainer(ParameterInfo parameter, FromContainerAttribute attribute)
    {
        var requestedType = parameter.ParameterType;

        if (attribute.ServiceType is not null)
        {
            if (!attribute.ServiceType.IsAssignableTo(parameter.ParameterType))
            {
                throw new InvalidOperationException(
                    $"Parameter '{parameter.Name}' of type '{parameter.ParameterType.FullName}' is not assignable from type '{attribute.ServiceType.FullName}' defined in {nameof(FromContainerAttribute)}.");
            }

            requestedType = attribute.ServiceType;
        }

        try
        {
            var service = string.IsNullOrWhiteSpace(attribute.ServiceName)
                ? services.GetRequiredService(requestedType)
                : services.GetRequiredKeyedService(requestedType, attribute.ServiceName);
            return service;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Parameter '{parameter.Name}' of type '{parameter.ParameterType.FullName}' could not be resolved from the container.",
                exception);
        }
    }

    private object ResolveParameterFromModel(ParameterInfo parameter, FromModelAttribute attribute, TestCase? testCase)
    {
        if (testCase is null)
        {
            throw new InvalidOperationException($"Parameter '{parameter.Name}' is suppose to be created from model, but the activation is without test case context.");
        }

        if (testCase.Model is null)
        {
            throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from model, but no model is provided.");
        }

        var sourceJToken = testCase.Model;

        if (!string.IsNullOrWhiteSpace(attribute.JQuery))
        {
            var token = testCase.Model.SelectToken(attribute.JQuery);

            if (token is null)
            {
                throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from model by JPath query, but no token found for the query '{attribute.JQuery}'.");
            }

            if (token.Type is JTokenType.Array or JTokenType.Bytes or JTokenType.Constructor or JTokenType.Comment)
            {
                throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from model, but the token found for the JPath query '{attribute.JQuery}' is not a supported type.");
            }

            sourceJToken = token.Type == JTokenType.Property
                ? ((JProperty)token).Value
                : (JObject)token;
        }

        if (parameter.ParameterType.IsInterface)
        {
            var serializer = JsonSerializer.CreateDefault(); // TODO: [P2] use configurable serializer with null rules
            serializer.Converters.Add(new NewtonsoftProxyJsonConverter(proxyFactory, parameter.ParameterType));
            var modelInterface = testCase.Model.ToObject(parameter.ParameterType, serializer);

            if (modelInterface is null)
            {
                throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from model, but the interface proxy could not be created.");
            }

            return modelInterface;
        }

        var value = sourceJToken.ToObject(parameter.ParameterType);
        return value ?? throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from model, but the object/value could not be created.");
    }

    private object ResolveParameterFromFixture(ParameterInfo parameter, FromFixtureAttribute attribute, TestCase? testCase)
    {
        if (!string.IsNullOrWhiteSpace(attribute.Label))
        {
            if (!Fixtures.TryGetFixture(attribute.Label, out var specificFixture))
            {
                throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from fixture, but no fixture with label '{attribute.Label}' is found.");
            }

            if (!parameter.ParameterType.IsInstanceOfType(specificFixture))
            {
                throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from fixture, but the fixture with label '{attribute.Label}' is not assignable to the parameter type '{parameter.ParameterType.FullName}'.");
            }

            return specificFixture;
        }

        if (testCase is null)
        {
            throw new InvalidOperationException($"Parameter '{parameter.Name}' is suppose to be created from fixture without specific label, but the activation is without test case context.");
        }

        var fixtureObjects = Fixtures.GetFixtures(testCase.FullyQualifiedName);
        var fixture = fixtureObjects.FirstOrDefault(parameter.ParameterType.IsInstanceOfType);
        return fixture ?? throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from fixture, but no fixture with compatible type has been found.");
    }

    private object TryProxySteps(object target, Type targetType, object[] constructorArguments)
    {
        if (!targetType.IsTypeWithSteps())
        {
            return target;
        }

        var proxy = proxyFactory.CreateStepsProxy(targetType, target, constructorArguments);
        return proxy;
    }
}
