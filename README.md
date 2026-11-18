# Cluster JAM Desktop

Windows administration client built with C# and WPF.

## Requirements

- Windows and .NET SDK 11.0.100 (`global.json`).
- .NET 10 Windows Desktop runtime.
- Auth Service and its HTTPS gateway for manual authentication tests.
- An existing administrator account.

## Run

```powershell
dotnet build ClusterJAM.Desktop.slnx
dotnet run --project ClusterJAM.Desktop.App
```

## Configuration

`ClusterJAM.Desktop.App/appsettings.json` defaults to `https://localhost:8443/`.
Set `CLUSTERJAM_API_BASE_URL` to override the gateway address. Use its HTTPS
root URL without `/api/v1`, credentials or query parameters.

For local development, create an untracked `appsettings.local.json` alongside
`appsettings.json`. It replaces the default configuration:

```json
{
    "baseUrl": "https://localhost:8443/",
    "timeoutSeconds": 20,
    "trustedRootCertificatePath": "<absolute-path-to-development-ca.crt>"
}
```

The optional development CA applies only to HTTPS loopback connections within
this client. Hostname, signature and certificate validity checks remain active.
Its revocation mode is `NoCheck` because the development CA has no revocation
service. Other connections use Windows certificate trust. Local settings and
certificates are excluded from Git; local settings are excluded from publishing.

## Scope and structure

- **App:** WPF views, MVVM commands and startup composition.
- **Business:** administrator sign-in, password recovery and session management.
- **Data:** HTTP adapters, configuration, TLS and sanitized local diagnostics.
- **Tests:** authentication, contracts, WPF input/rendering and TLS validation.
- **tools:** repeatable HTTP and HTTPS smoke checks.

Only administrator identities can reach the authenticated screen. Recovery uses
the six-digit code defined by Auth v1. Tokens remain in memory and are discarded
on local sign-out, including when remote revocation cannot be confirmed. Sessions
renew before expiry; concurrent requests share a serialized renewal operation.

Operational modules such as orders, inventory and personalization are future work.

## Validation

```powershell
dotnet format ClusterJAM.Desktop.slnx --verify-no-changes --no-restore
dotnet test ClusterJAM.Desktop.slnx
```

Automated tests do not require the backend. The pinned Auth v1 OpenAPI snapshot
is in `ClusterJAM.Desktop.Tests/Contracts/auth.v1.json`.

With the backend running, optional smoke checks are available:

```powershell
./tools/Test-LocalAuth.ps1 -BaseUri http://localhost:8080/
dotnet run --project tools/ClusterJAM.Desktop.SmokeChecks -- https://localhost:8443/ '<development-ca-path>'
```

The HTTP script permits plain HTTP only on loopback. Smoke checks use invalid
synthetic input and do not create accounts, send email or change passwords.
