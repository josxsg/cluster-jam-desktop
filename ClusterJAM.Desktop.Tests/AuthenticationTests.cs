using ClusterJAM.Desktop.App.ViewModels;
using ClusterJAM.Desktop.Business.Authentication;
using ClusterJAM.Desktop.Business.Errors;
using Xunit;

namespace ClusterJAM.Desktop.Tests;

public sealed class AuthenticationTests
{
    [Theory]
    [InlineData("administrador", AuthScreen.Administration)]
    [InlineData("cliente", AuthScreen.Restricted)]
    public async Task LoginRoutesUsingServerRole(string role, AuthScreen expected)
    {
        var fake = new FakeAuthClient { Role = role };
        using var viewModel = ViewModel(fake);
        viewModel.Email = "admin@example.com";
        viewModel.Password = "Password123!";
        await viewModel.SubmitCommand.ExecuteAsync();
        Assert.Equal(expected, viewModel.Screen);
        Assert.Empty(viewModel.Password);
        Assert.False(viewModel.IsError);
    }

    [Fact]
    public async Task UnknownRoleNeverEntersPanel()
    {
        var service = new AuthenticationService(new FakeAuthClient { Role = "owner" });
        await Assert.ThrowsAsync<ApiException>(() => service.LoginAsync("admin@example.com", "password"));
        Assert.Null(service.Identity);
        await Assert.ThrowsAsync<ApiException>(() => service.GetAccessTokenAsync());
    }

    [Fact]
    public async Task ConcurrentRequestsRotateRefreshTokenOnce()
    {
        var fake = new FakeAuthClient { Expiration = DateTimeOffset.UtcNow.AddSeconds(20) };
        var service = new AuthenticationService(fake);
        await service.LoginAsync("admin@example.com", "password");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.BeforeRenewal = () => release.Task;
        var requests = Enumerable.Range(0, 12).Select(_ => service.GetAccessTokenAsync()).ToArray();
        release.SetResult();
        Assert.All(await Task.WhenAll(requests), token => Assert.Equal("rotated-access-token", token));
        Assert.Equal(1, fake.RenewalCount);
    }

    [Fact]
    public async Task InvalidRefreshDiscardsSessionAndReturnsToLogin()
    {
        var fake = new FakeAuthClient { Expiration = DateTimeOffset.UtcNow.AddSeconds(20) };
        var service = new AuthenticationService(fake);
        using var viewModel = new AuthenticationViewModel(service)
        {
            Email = "admin@example.com",
            Password = "password"
        };
        await viewModel.SubmitCommand.ExecuteAsync();
        fake.Failure = new ApiException("INVALID_REFRESH_TOKEN", 401);
        await viewModel.MaintainSessionAsync();
        Assert.Null(service.Identity);
        Assert.Equal(AuthScreen.Login, viewModel.Screen);
        Assert.True(viewModel.IsError);
        Assert.Equal(1, fake.RenewalCount);
    }

    [Fact]
    public async Task RenewalRechecksChangedRole()
    {
        var fake = new FakeAuthClient { Expiration = DateTimeOffset.UtcNow.AddSeconds(20) };
        var service = new AuthenticationService(fake);
        using var viewModel = new AuthenticationViewModel(service)
        {
            Email = "admin@example.com",
            Password = "password"
        };
        await viewModel.SubmitCommand.ExecuteAsync();
        fake.Role = "cliente";
        await viewModel.MaintainSessionAsync();
        Assert.False(service.Identity!.IsAdministrator);
        Assert.Equal(AuthScreen.Restricted, viewModel.Screen);
    }

    [Fact]
    public async Task UnauthorizedCurrentIdentityRenewsAndRetriesOnce()
    {
        var fake = new FakeAuthClient();
        var service = new AuthenticationService(fake);
        await service.LoginAsync("admin@example.com", "password");
        fake.FailNextIdentity = true;
        Assert.True((await service.CheckIdentityAsync()).IsAdministrator);
        Assert.Equal(1, fake.RenewalCount);
    }

    [Fact]
    public async Task TransientRefreshFailureKeepsSessionWithoutAutomaticRetry()
    {
        var fake = new FakeAuthClient { Expiration = DateTimeOffset.UtcNow.AddSeconds(20) };
        var service = new AuthenticationService(fake);
        await service.LoginAsync("admin@example.com", "password");
        fake.Failure = new ApiException("SERVICE_UNAVAILABLE", 503);
        await Assert.ThrowsAsync<ApiException>(() => service.GetAccessTokenAsync());
        Assert.NotNull(service.Identity);
        await Assert.ThrowsAsync<ApiException>(() => service.GetAccessTokenAsync());
        Assert.Equal(1, fake.RenewalCount);
    }

    [Fact]
    public async Task FailedRemoteLogoutStillDiscardsLocalSession()
    {
        var fake = new FakeAuthClient();
        var service = new AuthenticationService(fake);
        await service.LoginAsync("admin@example.com", "password");
        fake.Failure = new ApiException("CONNECTION_FAILED");
        await Assert.ThrowsAsync<ApiException>(() => service.LogoutAsync());
        Assert.Null(service.Identity);
        await Assert.ThrowsAsync<ApiException>(() => service.GetAccessTokenAsync());
    }

    [Fact]
    public async Task UnconfirmedAccountStaysOnAdministratorLogin()
    {
        using var viewModel = ViewModel(new FakeAuthClient { Failure = new ApiException("EMAIL_NOT_CONFIRMED", 403) });
        viewModel.Email = "admin@example.com";
        viewModel.Password = "password";
        await viewModel.SubmitCommand.ExecuteAsync();
        Assert.Equal(AuthScreen.Login, viewModel.Screen);
        Assert.True(viewModel.IsError);
        Assert.Contains("responsable", viewModel.Message);
        Assert.Empty(viewModel.Password);
        Assert.False(viewModel.ShowCode);
    }

    [Fact]
    public async Task RecoveryReturnsToLoginAndNeverAutomaticallySignsIn()
    {
        var fake = new FakeAuthClient();
        using var viewModel = ViewModel(fake);
        viewModel.NavigateCommand.Execute("RequestRecovery");
        viewModel.Email = "unknown@example.com";
        await viewModel.SubmitCommand.ExecuteAsync();
        Assert.Equal(AuthScreen.ResetPassword, viewModel.Screen);
        Assert.Contains("Si la cuenta", viewModel.Message);
        viewModel.Code = "012345";
        viewModel.Password = viewModel.PasswordConfirmation = "NewPassword123!";
        await viewModel.SubmitCommand.ExecuteAsync();
        Assert.Equal(AuthScreen.Login, viewModel.Screen);
        Assert.Equal(0, fake.LoginCount);
        Assert.Empty(viewModel.Code);
        Assert.Empty(viewModel.Password);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("１２３４５６")]
    [InlineData("12345a")]
    public void CodeValidationRejectsAnythingExceptSixAsciiDigits(string code) =>
        Assert.Throws<ArgumentException>(() => AuthValidation.Code(code));

    [Fact]
    public async Task RequestingAnotherRecoveryCodeKeepsPasswordResetFlow()
    {
        var fake = new FakeAuthClient();
        using var viewModel = ViewModel(fake);
        viewModel.Email = "admin@example.com";
        viewModel.NavigateCommand.Execute("ResetPassword");
        viewModel.Code = "012345";
        await viewModel.ResendCommand.ExecuteAsync();
        Assert.Equal(1, fake.RecoveryCount);
        Assert.Equal(AuthScreen.ResetPassword, viewModel.Screen);
        Assert.Contains("Si la cuenta", viewModel.Message);
        Assert.Empty(viewModel.Code);
    }

    [Fact]
    public async Task PasswordMismatchKeepsResetFormAndClearsSecrets()
    {
        using var viewModel = ViewModel(new FakeAuthClient());
        viewModel.NavigateCommand.Execute("ResetPassword");
        viewModel.Email = "admin@example.com";
        viewModel.Code = "012345";
        viewModel.Password = "NewPassword123!";
        viewModel.PasswordConfirmation = "DifferentPassword123!";
        await viewModel.SubmitCommand.ExecuteAsync();
        Assert.Equal(AuthScreen.ResetPassword, viewModel.Screen);
        Assert.True(viewModel.IsError);
        Assert.Contains("no coinciden", viewModel.Message);
        Assert.Empty(viewModel.Password);
        Assert.Empty(viewModel.PasswordConfirmation);
        Assert.Empty(viewModel.Code);
    }

    [Theory]
    [InlineData("Administration")]
    [InlineData("Restricted")]
    [InlineData("Register")]
    [InlineData("ConfirmEmail")]
    [InlineData("999")]
    public void NavigationRejectsProtectedAndUnsupportedFlows(string destination)
    {
        using var viewModel = ViewModel(new FakeAuthClient());
        viewModel.NavigateCommand.Execute(destination);
        Assert.Equal(AuthScreen.Login, viewModel.Screen);
    }

    [Fact]
    public void EnteringRecoveryClearsLoginSecretsAndOnlyShowsEmail()
    {
        using var viewModel = ViewModel(new FakeAuthClient());
        viewModel.Password = "secret";
        viewModel.NavigateCommand.Execute("RequestRecovery");
        Assert.Equal(AuthScreen.RequestRecovery, viewModel.Screen);
        Assert.Empty(viewModel.Password);
        Assert.True(viewModel.ShowEmail);
        Assert.False(viewModel.ShowPassword);
        Assert.False(viewModel.ShowPasswordConfirmation);
        Assert.False(viewModel.ShowCode);
    }

    private static AuthenticationViewModel ViewModel(FakeAuthClient client) => new(new AuthenticationService(client));
}
