using ClusterJAM.Desktop.Business.Errors;

namespace ClusterJAM.Desktop.App.ViewModels;

public static class AuthErrorMessages
{
    public static string For(ApiException error)
    {
        var message = error.Code switch
        {
            "INVALID_CREDENTIALS" => "El correo o la contraseña son incorrectos.",
            "EMAIL_NOT_CONFIRMED" => "Esta cuenta aún no está habilitada para iniciar sesión. "
                + "Solicita su revisión al responsable.",
            "INVALID_EMAIL" => "El proveedor no acepta este correo.",
            "WEAK_PASSWORD" => "La contraseña no cumple la política de seguridad del proveedor.",
            "SAME_PASSWORD" => "Elige una contraseña distinta. Solicita otro código si el anterior fue consumido.",
            "INVALID_RECOVERY_CODE" => "El código de recuperación no es válido, venció o ya se utilizó.",
            "PASSWORD_RECOVERY_NOT_ALLOWED" => "No se permite recuperar esta cuenta.",
            "INVALID_APP_ROLE" => "Esta cuenta no tiene un rol de aplicación válido.",
            "INVALID_REFRESH_TOKEN" or "INVALID_ACCESS_TOKEN" or "SESSION_EXPIRED" =>
                "Tu sesión terminó. Vuelve a iniciar sesión.",
            "SESSION_REFRESH_CONFLICT" => "Otra renovación está en curso. Espera antes de volver a intentar.",
            "TOO_MANY_REQUESTS" or "RATE_LIMITED" => "Demasiados intentos. Espera antes de volver a intentar.",
            "INVALID_REQUEST" or "VALIDATION_ERROR" or "UNPROCESSABLE_ENTITY" => "Revisa los datos del formulario.",
            "REQUEST_TIMEOUT" => "El servidor tardó demasiado. Revisa el resultado antes de volver a intentar.",
            "CONNECTION_FAILED" => "No se pudo conectar. Revisa la red, la URL del servidor y su certificado HTTPS.",
            "INVALID_RESPONSE" => "El servidor devolvió una respuesta inesperada.",
            "SERVICE_UNAVAILABLE" => "El servicio de autenticación no está disponible. Intenta más tarde.",
            _ => error.Status switch
            {
                401 => "Tu acceso no es válido. Vuelve a iniciar sesión.",
                403 => "No tienes permiso para realizar esta operación.",
                429 => "Demasiados intentos. Espera antes de volver a intentar.",
                >= 500 => "El servidor no está disponible. Intenta más tarde.",
                _ => "No se pudo completar la operación. Vuelve a intentarlo."
            }
        };
        return error.RetryAfter is { } delay
            ? $"{message} Espera al menos {Math.Ceiling(delay.TotalSeconds)} segundos."
            : message;
    }
}
