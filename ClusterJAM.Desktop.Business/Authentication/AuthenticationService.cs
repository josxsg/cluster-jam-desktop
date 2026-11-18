using ClusterJAM.Desktop.Business.Errors;

namespace ClusterJAM.Desktop.Business.Authentication;

public sealed class AuthenticationService(IAuthClient client, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private AuthSession? _session;
    private ApiException? _renewalFailure;

    public AuthIdentity? Identity
    {
        get; private set;
    }
    public event EventHandler? SessionChanged;

    public Task LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        AuthValidation.Password(password, false);
        var normalizedEmail = AuthValidation.Email(email);
        return StartSessionAsync(token => client.LoginAsync(normalizedEmail, password, token), cancellationToken);
    }

    public Task RequestRecoveryAsync(string email, CancellationToken cancellationToken = default) =>
        client.RequestPasswordRecoveryAsync(AuthValidation.Email(email), cancellationToken);

    public async Task RecoverPasswordAsync(string email, string code, string password,
        CancellationToken cancellationToken = default)
    {
        AuthValidation.Password(password, true);
        await client.RecoverPasswordAsync(AuthValidation.Email(email), AuthValidation.Code(code), password,
            cancellationToken);
        await ForgetSessionAsync(cancellationToken);
    }

    private async Task StartSessionAsync(Func<CancellationToken, Task<AuthSession>> authenticate,
        CancellationToken cancellationToken)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            ClearSession();
            var session = await authenticate(cancellationToken);
            // Authorization comes from the current server identity, never an unverified JWT claim.
            var identity = await client.GetIdentityAsync(session.AccessToken, cancellationToken);
            ValidateIdentity(session, identity);
            _session = session;
            Identity = identity;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async Task<AuthIdentity> CheckIdentityAsync(CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            // This explicit user action allows one new attempt after a transient renewal failure.
            _renewalFailure = null;
            await EnsureSessionAsync(cancellationToken);
            try
            {
                Identity = await client.GetIdentityAsync(_session!.AccessToken, cancellationToken);
            }
            catch (ApiException error) when (error.Status == 401)
            {
                await RenewCoreAsync(cancellationToken);
                Identity = await client.GetIdentityAsync(_session!.AccessToken, cancellationToken);
            }
            ValidateIdentity(_session!, Identity);
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return Identity;
        }
        catch (ApiException error) when (error.Status is 401 or 403)
        {
            ClearSession();
            throw;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    // Shared by future authenticated API adapters. Concurrent callers rotate a refresh token only once.
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSessionAsync(cancellationToken);
            return _session!.AccessToken;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task EnsureSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is null)
        {
            throw new ApiException("SESSION_EXPIRED", 401);
        }
        if (_renewalFailure is not null)
        {
            throw _renewalFailure;
        }
        if (_session.ExpiresAt <= _timeProvider.GetUtcNow().AddSeconds(60))
        {
            await RenewCoreAsync(cancellationToken);
        }
    }

    private async Task RenewCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await client.RenewAsync(_session!.RefreshToken, cancellationToken);
            if (renewed.UserId != _session.UserId)
            {
                throw new ApiException("INVALID_RESPONSE", 401);
            }
            _session = renewed;
            var identity = await client.GetIdentityAsync(renewed.AccessToken, cancellationToken);
            ValidateIdentity(renewed, identity);
            Identity = identity;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (ApiException error) when (error.Status is 401 or 403)
        {
            ClearSession();
            throw;
        }
        catch (ApiException error)
        {
            _renewalFailure = error;
            throw;
        }
        // No automatic POST retries: the provider may already have consumed the refresh token.
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            var session = _session;
            ClearSession();
            if (session is not null)
            {
                // Even a 401 cannot prove remote revocation: an expired access token may have
                // an active refresh token. Let presentation distinguish local close from revocation.
                await client.LogoutAsync(session.AccessToken, cancellationToken);
            }
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async Task ForgetSessionAsync(CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            ClearSession();
        }
        finally { _sessionGate.Release(); }
    }

    private static void ValidateIdentity(AuthSession session, AuthIdentity identity)
    {
        if (identity.UserId != session.UserId || identity.AppRole is not ("cliente" or "administrador"))
        {
            throw new ApiException("INVALID_APP_ROLE", 403);
        }
    }

    private void ClearSession()
    {
        _session = null;
        _renewalFailure = null;
        Identity = null;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}
