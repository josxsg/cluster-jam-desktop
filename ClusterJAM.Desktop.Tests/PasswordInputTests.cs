using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClusterJAM.Desktop.App;
using ClusterJAM.Desktop.App.ViewModels;
using ClusterJAM.Desktop.Business.Authentication;
using ClusterJAM.Desktop.Business.Errors;
using ClusterJAM.Desktop.Data.Http;
using Xunit;

namespace ClusterJAM.Desktop.Tests;

public sealed class PasswordInputTests
{
    [Theory]
    [InlineData(null, 0, "", AuthScreen.Administration)]
    [InlineData("CONNECTION_FAILED", 0, "No se pudo conectar", AuthScreen.Login)]
    [InlineData("INVALID_CREDENTIALS", 401, "El correo o la contraseña son incorrectos", AuthScreen.Login)]
    public void FirstPasswordEntryReachesLoginAndShowsItsActualResult(string? code, int status,
        string expectedMessage, AuthScreen expectedScreen)
    {
        RunOnStaThread(() =>
        {
            var fake = new FakeAuthClient { Failure = code is null ? null : new ApiException(code, status) };
            using var viewModel = new AuthenticationViewModel(new AuthenticationService(fake));
            var window = PrepareWindow(viewModel);
            try
            {
                var email = (TextBox)window.FindName("EmailInput");
                var password = (PasswordBox)window.FindName("PasswordInput");
                Assert.Empty(viewModel.Password);
                email.Text = "admin@example.com";
                password.Password = "SyntheticPassword123!";
                Assert.Equal(password.Password, viewModel.Password);

                viewModel.SubmitCommand.ExecuteAsync().GetAwaiter().GetResult();

                Assert.Equal(1, fake.LoginCount);
                Assert.Equal(expectedScreen, viewModel.Screen);
                Assert.Equal(code is not null, viewModel.IsError);
                if (code is not null)
                {
                    Assert.Contains(expectedMessage, viewModel.Message);
                    Assert.DoesNotContain("Escribe tu contraseña", viewModel.Message);
                }
                Assert.Empty(password.Password);
                Assert.Empty(viewModel.Password);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void FirstPasswordEntriesReachPasswordRecovery()
    {
        RunOnStaThread(() =>
        {
            using var viewModel = new AuthenticationViewModel(new AuthenticationService(new FakeAuthClient()));
            viewModel.NavigateCommand.Execute("ResetPassword");
            var window = PrepareWindow(viewModel);
            try
            {
                ((TextBox)window.FindName("EmailInput")).Text = "admin@example.com";
                ((TextBox)window.FindName("CodeInput")).Text = "012345";
                ((PasswordBox)window.FindName("PasswordInput")).Password = "NewSyntheticPassword123!";
                ((PasswordBox)window.FindName("ConfirmationInput")).Password = "NewSyntheticPassword123!";

                viewModel.SubmitCommand.ExecuteAsync().GetAwaiter().GetResult();

                Assert.Equal(AuthScreen.Login, viewModel.Screen);
                Assert.False(viewModel.IsError);
                Assert.Contains("Contraseña actualizada", viewModel.Message);
                Assert.Empty(viewModel.Password);
                Assert.Empty(viewModel.PasswordConfirmation);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void EmptyPasswordShowsValidationWithoutCallingServer()
    {
        RunOnStaThread(() =>
        {
            var fake = new FakeAuthClient { Failure = new ApiException("CONNECTION_FAILED") };
            using var viewModel = new AuthenticationViewModel(new AuthenticationService(fake));
            var window = PrepareWindow(viewModel);
            try
            {
                ((TextBox)window.FindName("EmailInput")).Text = "admin@example.com";
                viewModel.SubmitCommand.ExecuteAsync().GetAwaiter().GetResult();
                Assert.Equal(0, fake.LoginCount);
                Assert.True(viewModel.IsError);
                Assert.Equal("Escribe tu contraseña.", viewModel.Message);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void OfflineLoginCanBeRetriedAfterPasswordIsCleared()
    {
        RunOnStaThread(() =>
        {
            var transport = new OfflineTransport();
            using var http = new HttpClient(transport) { BaseAddress = new Uri("https://localhost:8443/") };
            using var viewModel = new AuthenticationViewModel(new AuthenticationService(new AuthHttpClient(http)));
            var window = PrepareWindow(viewModel);
            try
            {
                ((TextBox)window.FindName("EmailInput")).Text = "admin@example.com";
                var password = (PasswordBox)window.FindName("PasswordInput");
                foreach (var attempt in new[] { 1, 2 })
                {
                    password.Password = "SyntheticPassword123!";
                    Assert.Equal(password.Password, viewModel.Password);
                    viewModel.SubmitCommand.ExecuteAsync().GetAwaiter().GetResult();
                    Assert.Equal(attempt, transport.RequestCount);
                    Assert.Equal(AuthScreen.Login, viewModel.Screen);
                    Assert.True(viewModel.IsError);
                    Assert.Contains("No se pudo conectar", viewModel.Message);
                    Assert.DoesNotContain("Escribe tu contraseña", viewModel.Message);
                    Assert.Empty(password.Password);
                }
            }
            finally { window.Close(); }
        });
    }

    private static MainWindow PrepareWindow(AuthenticationViewModel viewModel)
    {
        var window = new MainWindow(viewModel);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1120, 740));
        content.Arrange(new Rect(0, 0, 1120, 740));
        content.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        return window;
    }

    private static void RunOnStaThread(Action assertion)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                assertion();
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF input test timed out.");
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class OfflineTransport : HttpMessageHandler
    {
        public int RequestCount
        {
            get; private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new HttpRequestException("Synthetic connection refusal.");
        }
    }
}
