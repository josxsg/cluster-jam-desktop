using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ClusterJAM.Desktop.Data.Configuration;
using ClusterJAM.Desktop.Data.Http;
using Xunit;

namespace ClusterJAM.Desktop.Tests;

public sealed class TlsTrustTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("wrong-root", false)]
    [InlineData("wrong-hostname", false)]
    [InlineData("expired", false)]
    public async Task ExplicitLocalCaPreservesTlsValidation(string scenario, bool expectedSuccess)
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=Cluster JAM TLS Test CA", rootKey,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-7),
            DateTimeOffset.UtcNow.AddDays(7));
        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=localhost", leafKey,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(scenario == "wrong-hostname" ? "wrong-host.invalid" : "localhost");
        leafRequest.CertificateExtensions.Add(names.Build());
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var publicLeaf = leafRequest.Create(root, DateTimeOffset.UtcNow.AddDays(-2),
            scenario == "expired" ? DateTimeOffset.UtcNow.AddDays(-1) : DateTimeOffset.UtcNow.AddDays(1),
            RandomNumberGenerator.GetBytes(16));
        using var ephemeralLeaf = publicLeaf.CopyWithPrivateKey(leafKey);
        // Schannel needs an imported key handle for server credentials on Windows.
        // DefaultKeySet removes the temporary key when the certificate is disposed.
        using var leaf = X509CertificateLoader.LoadPkcs12(ephemeralLeaf.Export(X509ContentType.Pfx), null);
        using var wrongRootKey = RSA.Create(2048);
        var wrongRootRequest = new CertificateRequest("CN=Unrelated Test CA", wrongRootKey,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        wrongRootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var wrongRoot = wrongRootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var directory = Directory.CreateTempSubdirectory("clusterjam-tls-");
        var caPath = Path.Combine(directory.FullName, "test-ca.crt");
        File.WriteAllText(caPath, (scenario == "wrong-root" ? wrongRoot : root).ExportCertificatePem());
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var server = ServeAsync(listener, leaf, timeout.Token);
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var http = ApiHttpClientFactory.Create(new ApiSettings
            {
                BaseUrl = $"https://localhost:{port}/",
                TimeoutSeconds = 5,
                TrustedRootCertificatePath = caPath
            });
            if (expectedSuccess)
            {
                Assert.Equal("OK", await http.GetStringAsync("/", timeout.Token));
            }
            else
            {
                await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync("/", timeout.Token));
            }
            await server;
        }
        finally
        {
            timeout.Cancel();
            listener.Stop();
            File.Delete(caPath);
            directory.Delete();
        }
    }

    [Fact]
    public void DevelopmentTrustCannotBeEnabledForRemoteServers() =>
        Assert.Throws<InvalidDataException>(() => ApiHttpClientFactory.Create(new ApiSettings
        {
            BaseUrl = "https://example.com/",
            TrustedRootCertificatePath = "some-ca.crt"
        }));

    private static async Task ServeAsync(TcpListener listener, X509Certificate2 certificate, CancellationToken token)
    {
        using var client = await listener.AcceptTcpClientAsync(token);
        await using var tls = new SslStream(client.GetStream());
        try
        {
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate
            }, token);
            var buffer = new byte[4096];
            _ = await tls.ReadAsync(buffer, token);
            await tls.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK"), token);
        }
        catch (Exception error) when (error is AuthenticationException or IOException)
        {
            // A peer correctly rejecting the certificate can end the server handshake or stream.
        }
    }
}
