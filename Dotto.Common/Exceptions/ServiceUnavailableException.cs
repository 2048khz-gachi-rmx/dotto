namespace Dotto.Common.Exceptions;

public class ServiceUnavailableException : Exception
{
    public string ServiceName { get; }

    public ServiceUnavailableException(string serviceName)
    {
        ServiceName = serviceName;
    }
}