using _42.Platform.Sdk.Api;

namespace _42.Platform.Sdk;

public class ApiFactory : IApiFactory
{
    private readonly IRestClientFactory _clientFactory;
    private readonly IConfigurationFactory _configurationFactory;
    private readonly IExceptionFactoryProvider _exceptionFactoryProvider;

    public ApiFactory(
        IRestClientFactory clientFactory,
        IConfigurationFactory configurationFactory,
        IExceptionFactoryProvider exceptionFactoryProvider)
    {
        _clientFactory = clientFactory;
        _configurationFactory = configurationFactory;
        _exceptionFactoryProvider = exceptionFactoryProvider;
    }

    public IAnnotationsApi GetAnnotationsApi()
    {
        var configuration = _configurationFactory.BuildConfiguration();
        var clientAsync = _clientFactory.BuildAsynchronousClient(configuration);
        var clientSync = _clientFactory.BuildSynchronousClient(configuration);

        return new AnnotationsApi(clientSync, clientAsync, configuration)
        {
            ExceptionFactory = _exceptionFactoryProvider.GetExceptionFactory(),
        };
    }

    public IConfigurationApi GetConfigurationApi()
    {
        var configuration = _configurationFactory.BuildConfiguration();
        var clientAsync = _clientFactory.BuildAsynchronousClient(configuration);
        var clientSync = _clientFactory.BuildSynchronousClient(configuration);

        return new ConfigurationApi(clientSync, clientAsync, configuration)
        {
            ExceptionFactory = _exceptionFactoryProvider.GetExceptionFactory(),
        };
    }

    public IAccessApi GetAccessApi()
    {
        var configuration = _configurationFactory.BuildConfiguration();
        var clientAsync = _clientFactory.BuildAsynchronousClient(configuration);
        var clientSync = _clientFactory.BuildSynchronousClient(configuration);

        return new AccessApi(clientSync, clientAsync, configuration)
        {
            ExceptionFactory = _exceptionFactoryProvider.GetExceptionFactory(),
        };
    }
}
