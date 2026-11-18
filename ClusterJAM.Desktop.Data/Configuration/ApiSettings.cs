using System.Text.Json;

namespace ClusterJAM.Desktop.Data.Configuration;

public sealed class ApiSettings
{
    public string BaseUrl { get; set; } = "https://localhost:8443/";
    public int TimeoutSeconds { get; set; } = 20;
    public string? TrustedRootCertificatePath
    {
        get; set;
    }

    public static ApiSettings Load(string path)
    {
        var localPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "appsettings.local.json");
        var selectedPath = File.Exists(localPath) ? localPath : path;
        var settings = File.Exists(selectedPath)
            ? JsonSerializer.Deserialize<ApiSettings>(File.ReadAllText(selectedPath), JsonSerializerOptions.Web)
                ?? throw new InvalidDataException("La configuración está vacía.")
            : new ApiSettings();
        settings.BaseUrl = Environment.GetEnvironmentVariable("CLUSTERJAM_API_BASE_URL") ?? settings.BaseUrl;
        settings.Validate();
        return settings;
    }

    public Uri GetBaseUri()
    {
        Validate();
        return new Uri(BaseUrl);
    }

    private void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
        {
            throw new InvalidDataException(
                "Configura la URL HTTPS del proxy, sin /api/v1, credenciales ni parámetros.");
        }
        if (TimeoutSeconds is < 1 or > 120)
        {
            throw new InvalidDataException("El tiempo límite debe estar entre 1 y 120 segundos.");
        }
        if (!string.IsNullOrWhiteSpace(TrustedRootCertificatePath) && !uri.IsLoopback)
        {
            throw new InvalidDataException("La CA de desarrollo solo puede usarse con un servidor HTTPS local.");
        }
    }
}
