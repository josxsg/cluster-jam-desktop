using System.Net.Mail;

namespace ClusterJAM.Desktop.Business.Authentication;

public static class AuthValidation
{
    public static string Email(string value)
    {
        var email = value.Trim();
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var address) || address.Address != email
            || !address.Host.Contains('.'))
        {
            throw new ArgumentException("Escribe un correo de acceso válido.");
        }
        return email;
    }

    public static void Password(string value, bool isNew)
    {
        if (string.IsNullOrEmpty(value) || (isNew && value.Length < 8))
        {
            throw new ArgumentException(isNew
                ? "La contraseña debe tener al menos 8 caracteres."
                : "Escribe tu contraseña.");
        }
    }

    public static string Code(string value)
    {
        var code = value.Trim();
        if (code.Length != 6 || code.Any(character => character is < '0' or > '9'))
        {
            throw new ArgumentException("Escribe el código de seis dígitos que recibiste por correo.");
        }
        return code;
    }
}
