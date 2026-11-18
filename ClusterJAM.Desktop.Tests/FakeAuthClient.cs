using ClusterJAM.Desktop.Business.Authentication;
using ClusterJAM.Desktop.Business.Errors;

namespace ClusterJAM.Desktop.Tests;

internal sealed class FakeAuthClient : IAuthClient
{
    public static readonly Guid UserId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public string Role { get; set; } = "administrador";
    public DateTimeOffset Expiration { get; set; } = DateTimeOffset.UtcNow.AddHours(1);
    public ApiException? Failure
    {
        get; set;
    }
    public int RenewalCount
    {
        get; private set;
    }
    public int LoginCount
    {
        get; private set;
    }
    public int RecoveryCount
    {
        get; private set;
    }
    public Func<Task>? BeforeRenewal
    {
        get; set;
    }
    public bool FailNextIdentity
    {
        get; set;
    }

    private void ThrowIfFailed()
    {
        if (Failure is not null)
        {
            throw Failure;
        }
    }

    private AuthSession Session(string accessToken = "access-token") => new()
    {
        UserId = UserId,
        Email = "admin@example.com",
        AccessToken = accessToken,
        RefreshToken = "refresh-token",
        ExpiresAt = Expiration
    };

    public Task<AuthSession> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        LoginCount++;
        ThrowIfFailed();
        return Task.FromResult(Session());
    }

    public Task RequestPasswordRecoveryAsync(string email, CancellationToken cancellationToken)
    {
        ThrowIfFailed();
        RecoveryCount++;
        return Task.CompletedTask;
    }

    public Task RecoverPasswordAsync(string email, string code, string newPassword, CancellationToken cancellationToken)
    {
        ThrowIfFailed();
        return Task.CompletedTask;
    }

    public async Task<AuthSession> RenewAsync(string refreshToken, CancellationToken cancellationToken)
    {
        RenewalCount++;
        if (BeforeRenewal is not null)
        {
            await BeforeRenewal();
        }
        ThrowIfFailed();
        Expiration = DateTimeOffset.UtcNow.AddHours(1);
        return Session("rotated-access-token");
    }

    public Task<AuthIdentity> GetIdentityAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (FailNextIdentity)
        {
            FailNextIdentity = false;
            throw new ApiException("INVALID_ACCESS_TOKEN", 401);
        }
        ThrowIfFailed();
        return Task.FromResult(new AuthIdentity(UserId, "admin@example.com", Role));
    }

    public Task LogoutAsync(string accessToken, CancellationToken cancellationToken)
    {
        ThrowIfFailed();
        return Task.CompletedTask;
    }
}
