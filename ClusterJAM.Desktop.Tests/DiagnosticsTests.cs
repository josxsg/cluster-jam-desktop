using System.IO;
using System.Net.Http;
using System.Text.Json;
using ClusterJAM.Desktop.Business.Errors;
using ClusterJAM.Desktop.Data.Diagnostics;
using ClusterJAM.Desktop.Data.Http;
using Xunit;

namespace ClusterJAM.Desktop.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public async Task RequestTimeoutIsBoundedAndDoesNotRetry()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            calls++;
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage();
        }))
        {
            BaseAddress = new Uri("https://clusterjam.example/"),
            Timeout = TimeSpan.FromMilliseconds(100)
        };
        var error = await Assert.ThrowsAsync<ApiException>(() =>
            new AuthHttpClient(http).RenewAsync("synthetic-refresh-token", CancellationToken.None));
        Assert.Equal("REQUEST_TIMEOUT", error.Code);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FrameworkLoggerWritesOnlySafeTechnicalFields()
    {
        var directory = Directory.CreateTempSubdirectory("clusterjam-diagnostics-");
        try
        {
            using var diagnostics = new LocalDiagnostics(directory.FullName);
            using var http = new HttpClient(new Handler((_, _) => throw new HttpRequestException("sensitive-detail")))
            {
                BaseAddress = new Uri("https://clusterjam.example/")
            };
            await Assert.ThrowsAsync<ApiException>(() => new AuthHttpClient(http, diagnostics: diagnostics.Write)
                .LoginAsync("private@example.com", "synthetic-secret-password", CancellationToken.None));
            var line = File.ReadAllText(Path.Combine(directory.FullName, "desktop.jsonl"));
            using var json = JsonDocument.Parse(line);
            Assert.Equal("/api/v1/sessions", json.RootElement.GetProperty("http_route").GetString());
            Assert.Equal(0, json.RootElement.GetProperty("status_code").GetInt32());
            Assert.DoesNotContain("private@example.com", line);
            Assert.DoesNotContain("synthetic-secret-password", line);
            Assert.DoesNotContain("sensitive-detail", line);
            Assert.DoesNotContain("https://", line);
            Assert.DoesNotContain("Authorization", line);
        }
        finally
        {
            File.Delete(Path.Combine(directory.FullName, "desktop.jsonl"));
            directory.Delete();
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
