using System.Diagnostics;
using System.Text.Json;
using ClusterJAM.Desktop.Data.Http;

namespace ClusterJAM.Desktop.Data.Diagnostics;

public sealed class LocalDiagnostics : IDisposable
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly TraceSource _source = new("ClusterJAM.Desktop", SourceLevels.Information);

    public LocalDiagnostics(string directory)
    {
        _directory = directory;
        _source.Listeners.Clear();
        _source.Listeners.Add(new DiagnosticListener(this));
    }

    public void Write(HttpDiagnostic diagnostic) => _source.TraceData(
        diagnostic.Status >= 500 || diagnostic.Status == 0 ? TraceEventType.Warning : TraceEventType.Information,
        1, diagnostic);

    private void WriteFile(HttpDiagnostic diagnostic)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, "desktop.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length >= 1_000_000)
                {
                    File.Move(path, Path.Combine(_directory, "desktop.previous.jsonl"), true);
                }
                File.AppendAllText(path, JsonSerializer.Serialize(new
                {
                    timestamp = DateTimeOffset.UtcNow,
                    level = diagnostic.Status >= 500 || diagnostic.Status == 0 ? "WARN" : "INFO",
                    service_name = "clusterjam-desktop",
                    service_version = typeof(LocalDiagnostics).Assembly.GetName().Version?.ToString(),
                    environment = "desktop",
                    instance_id = Environment.ProcessId.ToString(),
                    @event = "http.request.completed",
                    message = "HTTP request completed",
                    trace_id = diagnostic.TraceId,
                    span_id = diagnostic.SpanId,
                    http_method = diagnostic.Method,
                    http_route = diagnostic.Route,
                    status_code = diagnostic.Status,
                    duration_ms = diagnostic.DurationMilliseconds
                }) + Environment.NewLine);
            }
        }
        catch (IOException) { /* Diagnostics must not prevent authentication. */ }
        catch (UnauthorizedAccessException) { /* Local logging may be disabled by the OS. */ }
    }

    public void Dispose() => _source.Close();

    private sealed class DiagnosticListener(LocalDiagnostics owner) : TraceListener
    {
        public override void TraceData(TraceEventCache? eventCache, string source, TraceEventType eventType,
            int id, object? data)
        {
            if (data is HttpDiagnostic diagnostic)
            {
                owner.WriteFile(diagnostic);
            }
        }

        // Ignore arbitrary text. Only allow-listed diagnostic records can reach the local log.
        public override void Write(string? message)
        {
        }
        public override void WriteLine(string? message)
        {
        }
    }
}
