namespace ClusterJAM.Desktop.Business.Authentication;

public interface IAuthClient
{
    Task<AuthSession> LoginAsync(string email, string password, CancellationToken cancellationToken);
    Task RequestPasswordRecoveryAsync(string email, CancellationToken cancellationToken);
    Task RecoverPasswordAsync(string email, string code, string newPassword, CancellationToken cancellationToken);
    Task<AuthSession> RenewAsync(string refreshToken, CancellationToken cancellationToken);
    Task<AuthIdentity> GetIdentityAsync(string accessToken, CancellationToken cancellationToken);
    Task LogoutAsync(string accessToken, CancellationToken cancellationToken);
}
