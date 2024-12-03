using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.nHolistic;

public class TypeActivator(
    IServiceProvider services,
    IFixtureProvider fixtures,
    IProxyFactory proxyFactory)
    : ITypeActivator
{
    public TType Activate<TType>(TestCase testCase)
    {
        var type = typeof(TType);
        return (TType)Activate(testCase, type);
    }

    public object Activate(TestCase testCase, Type type)
    {
        var constructors = type.GetConstructors();

        switch (constructors.Length)
        {
            case 0:
                throw new InvalidOperationException($"Type '{type.FullName}' does not have a public constructor.");

            case > 1:
                throw new InvalidOperationException($"Type '{type.FullName}' has more than one public constructor.");
        }

        var constructor = constructors[0];
        var arguments = ResolveParameters(testCase, constructor.GetParameters());
        return constructor.Invoke(arguments);
    }

    public object[] ResolveParameters(TestCase testCase, ParameterInfo[] parameters)
    {
        var resultParameters = new object[parameters.Length];

        foreach (var parameter in parameters)
        {
            resultParameters[parameter.Position] = ResolveParameter(testCase, parameter);
        }

        return resultParameters;
    }

    public object ResolveParameter(TestCase testCase, ParameterInfo parameter)
    {
        var fromAttributes = parameter.GetCustomAttributes<InjectionAttribute>().ToArray();

        if (fromAttributes.Length > 1)
        {
            throw new InvalidOperationException(
                $"The parameter '{parameter.Name}' has more than one injection attribute.");
        }

        if (fromAttributes.Length == 0)
        {
            return ResolveGenericParameter(testCase, parameter);
        }

        return fromAttributes[0].InjectionType switch
        {
            InjectionType.Container => ResolveParameterFromContainer(parameter, (FromContainerAttribute)fromAttributes[0]),
            InjectionType.Model => ResolveParameterFromModel(testCase, parameter, (FromModelAttribute)fromAttributes[0]),
            InjectionType.Fixture => ResolveParameterFromFixture(testCase, parameter, (FromFixtureAttribute)fromAttributes[0]),
            _ => throw new InvalidOperationException($"Parameter '{parameter.Name}' has unsupported injection attribute '{fromAttributes[0].InjectionType}'."),
        };
    }

    private object ResolveGenericParameter(TestCase testCase, ParameterInfo parameter)
    {
        var service = services.GetService(parameter.ParameterType);

        if (service is not null)
        {
            return service;
        }

        var fixtureObjects = fixtures.GetFixtures(testCase.FullyQualifiedName);
        var fixture = fixtureObjects.FirstOrDefault(parameter.ParameterType.IsInstanceOfType);

        if (fixture is not null)
        {
            return fixture;
        }

        if (testCase.Model is not null)
        {
            if (parameter.ParameterType is { IsClass: true, IsAbstract: false })
            {
                var modelClass = testCase.Model.ToObject(parameter.ParameterType);

                if (modelClass is not null)
                {
                    return modelClass;
                }
            }

            if (parameter.ParameterType.IsInterface)
            {
                var serializer = JsonSerializer.CreateDefault();
                serializer.Converters.Add(new NewtonsoftProxyJsonConverter(proxyFactory, parameter.ParameterType));
                var modelInterface = testCase.Model.ToObject(parameter.ParameterType, serializer);

                if (modelInterface is null)
                {
                    throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from model, but the model object could not be created.");
                }

                return modelInterface;
            }
        }

        throw new InvalidOperationException(
            $"Parameter '{parameter.Name}' of type '{parameter.ParameterType.FullName}' could not be resolved.");
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

    private object ResolveParameterFromModel(TestCase testCase, ParameterInfo parameter, FromModelAttribute attribute)
    {
        if (testCase.Model is null)
        {
            throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from model, but no model is provider.");
        }

        var sourceJObject = testCase.Model;

        if (!string.IsNullOrWhiteSpace(attribute.JQuery))
        {
            var token = testCase.Model.SelectToken(attribute.JQuery);

            if (token is null)
            {
                throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from model by JPath query, but no token found for the query '{attribute.JQuery}'.");
            }

            if (token.Type != JTokenType.Object)
            {
                throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from model, but the token found for the JPath query '{attribute.JQuery}' is not an object.");
            }

            sourceJObject = (JObject)token;
        }

        var modelObject = sourceJObject.ToObject(parameter.ParameterType);
        return modelObject ?? throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from model, but the model object could not be created.");
    }

    private object ResolveParameterFromFixture(TestCase testCase, ParameterInfo parameter, FromFixtureAttribute attribute)
    {
        if (!string.IsNullOrWhiteSpace(attribute.Label))
        {
            if (!fixtures.TryGetFixture(attribute.Label, out var specificFixture))
            {
                throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from fixture, but no fixture with label '{attribute.Label}' is found.");
            }

            if (!parameter.ParameterType.IsInstanceOfType(specificFixture))
            {
                throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from fixture, but the fixture with label '{attribute.Label}' is not assignable to the parameter type '{parameter.ParameterType.FullName}'.");
            }

            return specificFixture;
        }

        var fixtureObjects = fixtures.GetFixtures(testCase.FullyQualifiedName);
        var fixture = fixtureObjects.FirstOrDefault(parameter.ParameterType.IsInstanceOfType);
        return fixture ?? throw new InvalidOperationException($"Parameter '{parameter.Name}' should be activated from fixture, but no fixture has been found.");
    }
}
