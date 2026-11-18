namespace ClusterJAM.Desktop.Data.Http;

// Only allow-listed technical data. Never include bodies, headers, emails, URLs or exception messages.
public sealed record HttpDiagnostic(string Method, string Route, int Status, long DurationMilliseconds,
    string TraceId, string SpanId);
