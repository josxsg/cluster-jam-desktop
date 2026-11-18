namespace ClusterJAM.Desktop.Business.Errors;

public sealed class ApiException : Exception
{
    public ApiException(string code, int status = 0, string? traceId = null, TimeSpan? retryAfter = null)
        : base($"API operation failed ({code}, {status}).")
    {
        Code = code;
        Status = status;
        TraceId = traceId;
        RetryAfter = retryAfter;
    }

    public string Code
    {
        get;
    }
    public int Status
    {
        get;
    }
    public string? TraceId
    {
        get;
    }
    public TimeSpan? RetryAfter
    {
        get;
    }
}
