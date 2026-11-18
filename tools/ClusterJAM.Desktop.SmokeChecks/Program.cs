using ClusterJAM.Desktop.Business.Errors;
using ClusterJAM.Desktop.Data.Configuration;
using ClusterJAM.Desktop.Data.Http;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: SmokeChecks <https-local-base-url> <development-ca-pem-path>");
    return 2;
}

var settings = new ApiSettings { BaseUrl = args[0], TrustedRootCertificatePath = args[1] };
using var http = ApiHttpClientFactory.Create(settings);
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
var token = cancellation.Token;
var auth = new AuthHttpClient(http);
var passed = 0;
var total = 0;

await CheckAsync("HTTPS readiness", async () =>
{
    using var response = await http.GetAsync("health/auth/ready", token);
    response.EnsureSuccessStatusCode();
});
await ExpectErrorAsync("Login", () => auth.LoginAsync("clusterjam-smoke-nobody@example.invalid",
    "SyntheticUnusablePassword123!", token), 401, "INVALID_CREDENTIALS");
await ExpectErrorAsync("Recovery request validation", () => auth.RequestPasswordRecoveryAsync("invalid", token),
    422, "VALIDATION_ERROR");
await ExpectErrorAsync("Recovery validation", () => auth.RecoverPasswordAsync("invalid", "12345",
    "NewSyntheticPassword123!", token), 422, "VALIDATION_ERROR");
await ExpectErrorAsync("Renewal rejection", () => auth.RenewAsync("invalid-synthetic-refresh-token", token),
    401, "INVALID_REFRESH_TOKEN");
await ExpectErrorAsync("Identity rejection", () => auth.GetIdentityAsync("invalid-synthetic-access-token", token),
    401, "INVALID_ACCESS_TOKEN");
await ExpectErrorAsync("Logout rejection", () => auth.LogoutAsync("invalid-synthetic-access-token", token),
    401, "INVALID_ACCESS_TOKEN");

Console.WriteLine($"Live HTTPS client checks: {passed}/{total} passed.");
return passed == total ? 0 : 1;

async Task CheckAsync(string name, Func<Task> check)
{
    total++;
    try
    {
        await check();
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception error)
    {
        // Never print error messages or request/response bodies: they may contain credentials.
        Console.WriteLine($"FAIL {name}: {error.GetType().Name}");
    }
    await Task.Delay(300, token);
}

Task ExpectErrorAsync(string name, Func<Task> request, int status, string code) => CheckAsync(name, async () =>
{
    try
    {
        await request();
        throw new InvalidOperationException("An error response was expected.");
    }
    catch (ApiException error) when (error.Status == status && error.Code == code && error.TraceId?.Length == 32)
    {
        // A matching RFC 9457 response was parsed by the same adapter used by WPF.
    }
});
