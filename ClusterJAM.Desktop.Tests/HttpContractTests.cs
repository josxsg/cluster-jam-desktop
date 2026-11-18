using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClusterJAM.Desktop.Business.Errors;
using ClusterJAM.Desktop.Data.Configuration;
using ClusterJAM.Desktop.Data.Http;
using Xunit;

namespace ClusterJAM.Desktop.Tests;

public sealed class HttpContractTests
{
    [Theory]
    [InlineData("post", "/api/v1/sessions", 200)]
    [InlineData("post", "/api/v1/password-recovery-requests", 200)]
    [InlineData("post", "/api/v1/password-recoveries", 204)]
    [InlineData("post", "/api/v1/session-renewals", 200)]
    [InlineData("get", "/api/v1/sessions/current", 200)]
    [InlineData("delete", "/api/v1/sessions/current", 204)]
    public async Task EveryDesktopAuthOperationMatchesPinnedOpenApi(string method, string path, int status)
    {
        using var spec = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Contracts", "auth.v1.json")));
        var root = spec.RootElement;
        var operation = root.GetProperty("paths").GetProperty(path).GetProperty(method);
        var handler = new Handler(async (request, token) =>
        {
            Assert.Equal(method, request.Method.Method.ToLowerInvariant());
            Assert.Equal(path, request.RequestUri!.AbsolutePath);
            Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$",
                request.Headers.GetValues("traceparent").Single());
            var isProtected = operation.TryGetProperty("security", out _);
            Assert.Equal(isProtected ? "Bearer access-token" : null, request.Headers.Authorization?.ToString());
            if (operation.TryGetProperty("requestBody", out var requestBody))
            {
                Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
                using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(token));
                var schema = Resolve(root, requestBody.GetProperty("content").GetProperty("application/json")
                    .GetProperty("schema"));
                var names = body.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray();
                Assert.Equal(schema.GetProperty("required").EnumerateArray()
                    .Select(value => value.GetString()!).Order().ToArray(), names);
                Assert.All(body.RootElement.EnumerateObject(), property => Assert.Equal(JsonValueKind.String,
                    property.Value.ValueKind));
                if (body.RootElement.TryGetProperty("code", out var code))
                {
                    Assert.Equal("012345", code.GetString());
                }
                Assert.False(body.RootElement.TryGetProperty("appRole", out _));
            }
            else
            {
                Assert.Null(request.Content);
            }

            var responseSpec = operation.GetProperty("responses").GetProperty(status.ToString());
            var response = new HttpResponseMessage((HttpStatusCode)status);
            if (status != 204)
            {
                var schema = Resolve(root, responseSpec.GetProperty("content").GetProperty("application/json")
                    .GetProperty("schema"));
                var examples = schema.GetProperty("properties").EnumerateObject()
                    .ToDictionary(property => property.Name, property => property.Value.GetProperty("example"));
                response.Content = new StringContent(JsonSerializer.Serialize(examples), Encoding.UTF8,
                    "application/json");
            }
            return response;
        });
        using var http = CreateHttp(handler);
        var client = new AuthHttpClient(http);
        var cancellationToken = CancellationToken.None;
        switch ((method, path))
        {
            case (_, "/api/v1/sessions"):
                await client.LoginAsync("user@example.com", "Password123!", cancellationToken);
                break;
            case (_, "/api/v1/password-recovery-requests"):
                await client.RequestPasswordRecoveryAsync("user@example.com", cancellationToken);
                break;
            case (_, "/api/v1/password-recoveries"):
                await client.RecoverPasswordAsync("user@example.com", "012345", "NewPassword123!",
                    cancellationToken);
                break;
            case (_, "/api/v1/session-renewals"):
                await client.RenewAsync("refresh-token", cancellationToken);
                break;
            case ("get", _):
                await client.GetIdentityAsync("access-token", cancellationToken);
                break;
            case ("delete", _):
                await client.LogoutAsync("access-token", cancellationToken);
                break;
        }
    }

    [Fact]
    public async Task ProblemDetailsPreserveCodeTraceAndRetryAfterWithoutLeakingDetail()
    {
        using var http = CreateHttp(new Handler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""
                    {"code":"TOO_MANY_REQUESTS","traceId":"trace-123","detail":"secret-server-detail"}
                    """, Encoding.UTF8, "application/problem+json")
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return Task.FromResult(response);
        }));
        var error = await Assert.ThrowsAsync<ApiException>(() =>
            new AuthHttpClient(http).LoginAsync("user@example.com", "secret-password", CancellationToken.None));
        Assert.Equal("TOO_MANY_REQUESTS", error.Code);
        Assert.Equal("trace-123", error.TraceId);
        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Theory]
    [InlineData("application/problem+json", "{")]
    [InlineData("text/html", "<html>Bad gateway</html>")]
    public async Task MalformedProxyErrorsFallBackToStatus(string contentType, string body)
    {
        using var http = CreateHttp(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.BadGateway)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        })));
        var error = await Assert.ThrowsAsync<ApiException>(() =>
            new AuthHttpClient(http).LoginAsync("user@example.com", "password", CancellationToken.None));
        Assert.Equal(502, error.Status);
        Assert.Equal("HTTP_502", error.Code);
    }

    [Fact]
    public async Task InvalidSuccessfulSessionIsRejected()
    {
        using var http = CreateHttp(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        })));
        var error = await Assert.ThrowsAsync<ApiException>(() =>
            new AuthHttpClient(http).LoginAsync("user@example.com", "password", CancellationToken.None));
        Assert.Equal("INVALID_RESPONSE", error.Code);
    }

    [Fact]
    public async Task NetworkFailureIsNotRetried()
    {
        var calls = 0;
        using var http = CreateHttp(new Handler((_, _) =>
        {
            calls++;
            throw new HttpRequestException("sensitive-network-detail");
        }));
        var error = await Assert.ThrowsAsync<ApiException>(() =>
            new AuthHttpClient(http).RenewAsync("secret-refresh-token", CancellationToken.None));
        Assert.Equal("CONNECTION_FAILED", error.Code);
        Assert.Equal(1, calls);
        Assert.DoesNotContain("sensitive", error.ToString());
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsTimeout()
    {
        using var source = new CancellationTokenSource();
        using var http = CreateHttp(new Handler(async (_, token) =>
        {
            source.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage();
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AuthHttpClient(http).RenewAsync("refresh-token", source.Token));
    }

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("https://example.com/api/v1/")]
    [InlineData("https://user:password@example.com/")]
    [InlineData("https://example.com/?secret=value")]
    public void ConfigurationRejectsInsecureOrAmbiguousAddresses(string address) =>
        Assert.Throws<InvalidDataException>(() => new ApiSettings { BaseUrl = address }.GetBaseUri());

    private static JsonElement Resolve(JsonElement root, JsonElement schema) =>
        root.GetProperty("components").GetProperty("schemas")
            .GetProperty(schema.GetProperty("$ref").GetString()!.Split('/').Last());

    private static HttpClient CreateHttp(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://clusterjam.example/"),
        Timeout = TimeSpan.FromSeconds(5)
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
