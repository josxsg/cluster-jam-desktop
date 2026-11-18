using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClusterJAM.Desktop.Business.Authentication;
using ClusterJAM.Desktop.Business.Errors;

namespace ClusterJAM.Desktop.Data.Http;

public sealed class AuthHttpClient(HttpClient httpClient, TimeProvider? timeProvider = null,
    Action<HttpDiagnostic>? diagnostics = null) : IAuthClient
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<AuthSession> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
        SendSessionAsync("api/v1/sessions", new
        {
            email,
            password
        }, cancellationToken);

    public async Task RequestPasswordRecoveryAsync(string email, CancellationToken cancellationToken) =>
        _ = await SendAsync<MessageResponse>(HttpMethod.Post, "api/v1/password-recovery-requests", new
        {
            email
        },
            HttpStatusCode.OK, null, cancellationToken);

    public async Task RecoverPasswordAsync(string email, string code, string newPassword,
        CancellationToken cancellationToken) =>
        _ = await SendAsync<object>(HttpMethod.Post, "api/v1/password-recoveries", new
        {
            email,
            code,
            newPassword
        },
            HttpStatusCode.NoContent, null, cancellationToken);

    public Task<AuthSession> RenewAsync(string refreshToken, CancellationToken cancellationToken) =>
        SendSessionAsync("api/v1/session-renewals", new
        {
            refreshToken
        }, cancellationToken);

    public async Task<AuthIdentity> GetIdentityAsync(string accessToken, CancellationToken cancellationToken)
    {
        var identity = await SendAsync<AuthIdentity>(HttpMethod.Get, "api/v1/sessions/current", null,
            HttpStatusCode.OK, accessToken, cancellationToken);
        if (identity.UserId == Guid.Empty || string.IsNullOrWhiteSpace(identity.Email))
        {
            throw new ApiException("INVALID_RESPONSE");
        }
        if (identity.AppRole is not ("cliente" or "administrador"))
        {
            throw new ApiException("INVALID_APP_ROLE", 403);
        }
        return identity;
    }

    public async Task LogoutAsync(string accessToken, CancellationToken cancellationToken) =>
        _ = await SendAsync<object>(HttpMethod.Delete, "api/v1/sessions/current", null,
            HttpStatusCode.NoContent, accessToken, cancellationToken);

    private async Task<AuthSession> SendSessionAsync(string route, object body, CancellationToken cancellationToken)
    {
        var response = await SendAsync<SessionResponse>(HttpMethod.Post, route, body, HttpStatusCode.OK,
            null, cancellationToken);
        if (response.UserId == Guid.Empty || string.IsNullOrWhiteSpace(response.Email)
            || string.IsNullOrWhiteSpace(response.AccessToken) || string.IsNullOrWhiteSpace(response.RefreshToken)
            || response.RefreshToken.Length > 4096 || !double.IsFinite(response.ExpiresIn)
            || response.ExpiresIn is <= 0 or > 31536000
            || !string.Equals(response.TokenType, "bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiException("INVALID_RESPONSE");
        }
        return new AuthSession
        {
            UserId = response.UserId,
            Email = response.Email,
            AccessToken = response.AccessToken,
            RefreshToken = response.RefreshToken,
            ExpiresAt = _timeProvider.GetUtcNow().AddSeconds(response.ExpiresIn)
        };
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string route, object? body, HttpStatusCode expectedStatus,
        string? accessToken, CancellationToken cancellationToken)
    {
        using var activity = new Activity("clusterjam.http").SetIdFormat(ActivityIdFormat.W3C).Start();
        using var request = new HttpRequestMessage(method, route);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/problem+json"));
        request.Headers.TryAddWithoutValidation("traceparent", activity.Id);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonSerializerOptions.Web);
        }
        var started = Stopwatch.GetTimestamp();
        var status = 0;
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                throw await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
            }
            if (response.StatusCode != expectedStatus)
            {
                throw new ApiException("INVALID_RESPONSE", status);
            }
            if (expectedStatus == HttpStatusCode.NoContent)
            {
                return default!;
            }
            return await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, cancellationToken)
                .ConfigureAwait(false) ?? throw new ApiException("INVALID_RESPONSE", status);
        }
        catch (JsonException)
        {
            throw new ApiException("INVALID_RESPONSE", status);
        }
        catch (HttpRequestException)
        {
            throw new ApiException("CONNECTION_FAILED");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiException("REQUEST_TIMEOUT");
        }
        finally
        {
            diagnostics?.Invoke(new HttpDiagnostic(method.Method, "/" + route, status,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                activity.TraceId.ToString(), activity.SpanId.ToString()));
        }
    }

    private async Task<ApiException> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ProblemResponse? problem = null;
        if (response.Content.Headers.ContentType?.MediaType == "application/problem+json")
        {
            try
            {
                problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(JsonSerializerOptions.Web,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException) { /* Proxies can return malformed error bodies. Fall back to HTTP status. */ }
        }
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } date)
        {
            retryAfter = date - _timeProvider.GetUtcNow();
        }
        return new ApiException(problem?.Code ?? $"HTTP_{(int)response.StatusCode}", (int)response.StatusCode,
            problem?.TraceId, retryAfter > TimeSpan.Zero ? retryAfter : null);
    }

    private sealed class SessionResponse
    {
        public Guid UserId
        {
            get; set;
        }
        public string Email { get; set; } = "";
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public double ExpiresIn
        {
            get; set;
        }
        public string TokenType { get; set; } = "";
    }

    private sealed class MessageResponse
    {
        public string Message { get; set; } = "";
    }

    private sealed class ProblemResponse
    {
        public string? Code
        {
            get; set;
        }
        public string? TraceId
        {
            get; set;
        }
    }
}
