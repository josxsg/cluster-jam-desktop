using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ClusterJAM.Desktop.Data.Configuration;

namespace ClusterJAM.Desktop.Data.Http;

public static class ApiHttpClientFactory
{
    public static HttpClient Create(ApiSettings settings)
    {
        var baseUri = settings.GetBaseUri();
        var sockets = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        HttpMessageHandler handler = sockets;
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.TrustedRootCertificatePath))
            {
                var root = X509Certificate2.CreateFromPem(File.ReadAllText(settings.TrustedRootCertificatePath));
                handler = new CertificateOwnerHandler(sockets, root);
                var now = DateTime.UtcNow;
                if (!root.Extensions.OfType<X509BasicConstraintsExtension>().Any(extension =>
                    extension.CertificateAuthority) || root.NotBefore.ToUniversalTime() > now
                    || root.NotAfter.ToUniversalTime() <= now
                    || !root.SubjectName.RawData.SequenceEqual(root.IssuerName.RawData))
                {
                    throw new InvalidDataException("La CA local debe ser una raíz de confianza válida y vigente.");
                }
                // Trust is limited to this client's explicit development CA. TLS still verifies
                // the server name, signatures, certificate usages and validity dates normally.
                // There is no RemoteCertificateValidationCallback and no Windows store modification.
                var chainPolicy = new X509ChainPolicy
                {
                    TrustMode = X509ChainTrustMode.CustomRootTrust,
                    VerificationFlags = X509VerificationFlags.NoFlag,
                    RevocationMode = X509RevocationMode.NoCheck
                };
                chainPolicy.CustomTrustStore.Add(root);
                sockets.SslOptions.CertificateChainPolicy = chainPolicy;
            }
            return new HttpClient(handler)
            {
                BaseAddress = baseUri,
                Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
                MaxResponseContentBufferSize = 64 * 1024
            };
        }
        catch (Exception error) when (error is IOException or CryptographicException)
        {
            handler.Dispose();
            throw new InvalidDataException("No se pudo cargar la CA local configurada.", error);
        }
    }

    private sealed class CertificateOwnerHandler(HttpMessageHandler innerHandler, X509Certificate2 root)
        : DelegatingHandler(innerHandler)
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                root.Dispose();
            }
        }
    }
}
