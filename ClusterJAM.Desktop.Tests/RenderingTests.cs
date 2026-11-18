using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClusterJAM.Desktop.App;
using ClusterJAM.Desktop.App.Infrastructure;
using ClusterJAM.Desktop.App.ViewModels;
using ClusterJAM.Desktop.Business.Authentication;
using Xunit;

namespace ClusterJAM.Desktop.Tests;

public sealed class RenderingTests
{
    [Theory]
    [InlineData("Login", 1120, 740)]
    [InlineData("RequestRecovery", 1120, 740)]
    [InlineData("ResetPassword", 1120, 740)]
    [InlineData("Restricted", 1120, 740)]
    [InlineData("Administration", 1120, 740)]
    [InlineData("ResetPassword", 820, 580)]
    public void AuthenticationScreensRenderWithoutBindingErrors(string screen, int width, int height)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Render(screen, width, height);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF render timed out.");
        Assert.Null(failure);
    }

    private static void Render(string screen, int width, int height)
    {
        using var listener = new BindingErrors();
        var source = PresentationTraceSources.DataBindingSource;
        source.Listeners.Add(listener);
        source.Switch.Level = SourceLevels.Error;
        try
        {
            var fake = new FakeAuthClient { Role = screen == "Restricted" ? "cliente" : "administrador" };
            using var viewModel = new AuthenticationViewModel(new AuthenticationService(fake));
            if (screen is "Administration" or "Restricted")
            {
                viewModel.Email = "admin@example.com";
                viewModel.Password = "Password123!";
                viewModel.SubmitCommand.ExecuteAsync().GetAwaiter().GetResult();
            }
            else
            {
                viewModel.NavigateCommand.Execute(screen);
            }
            var window = new MainWindow(viewModel);
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, height));
            content.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            // Verify the PasswordBox bridge in both directions and its clearing behavior.
            var password = (PasswordBox)window.FindName("PasswordInput");
            viewModel.Password = "synthetic-test-password";
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            Assert.Equal(viewModel.Password, password.Password);
            password.Password = "changed-test-password";
            Assert.Equal(password.Password, viewModel.Password);
            viewModel.Password = "";
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            Assert.Empty(password.Password);
            Assert.Empty(PasswordBinding.GetValue(password));
            Assert.Empty(listener.Errors);

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(directory);
            using var output = File.Create(Path.Combine(directory, $"{screen}-{width}.png"));
            encoder.Save(output);
            window.Close();
        }
        finally { source.Listeners.Remove(listener); }
    }

    private sealed class BindingErrors : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message)
        {
            if (message is not null)
            {
                Errors.Add(message);
            }
        }
        public override void WriteLine(string? message)
        {
            if (message is not null)
            {
                Errors.Add(message);
            }
        }
    }
}
