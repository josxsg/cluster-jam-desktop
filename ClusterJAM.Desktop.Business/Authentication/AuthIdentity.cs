namespace ClusterJAM.Desktop.Business.Authentication;

public sealed record AuthIdentity(Guid UserId, string Email, string AppRole)
{
    public bool IsAdministrator => AppRole == "administrador";
}
