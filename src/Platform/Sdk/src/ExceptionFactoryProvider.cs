using _42.Platform.Sdk.Client;

namespace _42.Platform.Sdk;

public class ExceptionFactoryProvider : IExceptionFactoryProvider
{
    private ExceptionFactory? _exceptionFactory;

    public ExceptionFactoryProvider()
    {
        // no operation
    }

    public ExceptionFactoryProvider(ExceptionFactory? exceptionFactory)
    {
        _exceptionFactory = exceptionFactory;
    }

    public ExceptionFactory GetExceptionFactory()
    {
        return _exceptionFactory ?? SdkConfiguration.DefaultExceptionFactory;
    }

    public void SetExceptionFactory(ExceptionFactory exceptionFactory)
    {
        _exceptionFactory = exceptionFactory;
    }
}
