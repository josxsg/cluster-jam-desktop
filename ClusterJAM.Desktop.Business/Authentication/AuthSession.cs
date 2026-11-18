namespace ClusterJAM.Desktop.Business.Authentication;

// Tokens are held only in memory. Do not use a record: its generated ToString exposes secrets.
public sealed class AuthSession
{
    public required Guid UserId
    {
        get; init;
    }
    public required string Email
    {
        get; init;
    }
    public required string AccessToken
    {
        get; init;
    }
    public required string RefreshToken
    {
        get; init;
    }
    public required DateTimeOffset ExpiresAt
    {
        get; init;
    }

    public override string ToString() => nameof(AuthSession);
}
