using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using ClusterJAM.Desktop.App.ViewModels;
using ClusterJAM.Desktop.Business.Authentication;
using ClusterJAM.Desktop.Data.Configuration;
using ClusterJAM.Desktop.Data.Diagnostics;
using ClusterJAM.Desktop.Data.Http;

namespace ClusterJAM.Desktop.App;

public partial class App : Application
{
    private HttpClient? _httpClient;
    private LocalDiagnostics? _diagnostics;

    protected override void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        try
        {
            var settings = ApiSettings.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
            // Composition root: only startup knows both Business and concrete Data adapters.
            _httpClient = ApiHttpClientFactory.Create(settings);
            _diagnostics = new LocalDiagnostics(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "ClusterJAM", "Logs"));
            IAuthClient client = new AuthHttpClient(_httpClient, diagnostics: _diagnostics.Write);
            var authentication = new AuthenticationService(client);
            MainWindow = new MainWindow(new AuthenticationViewModel(authentication));
            MainWindow.Show();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show("No se pudo leer la configuración. Revisa appsettings.json y CLUSTERJAM_API_BASE_URL. "
                + "Revisa también la CA local, si está configurada.", "Cluster JAM", MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs args)
    {
        _httpClient?.Dispose();
        _diagnostics?.Dispose();
        base.OnExit(args);
    }
}
