using ClusterJAM.Desktop.App.Infrastructure;
using ClusterJAM.Desktop.Business.Authentication;
using ClusterJAM.Desktop.Business.Errors;

namespace ClusterJAM.Desktop.App.ViewModels;

public sealed class AuthenticationViewModel : ObservableObject, IDisposable
{
    private readonly AuthenticationService _authentication;
    private readonly CancellationTokenSource _lifetime = new();
    private AuthScreen _screen;
    private string _email = "";
    private string _password = "";
    private string _passwordConfirmation = "";
    private string _code = "";
    private string _message = "";
    private bool _isError;
    private bool _isBusy;
    private bool _disposed;

    public AuthenticationViewModel(AuthenticationService authentication)
    {
        _authentication = authentication;
        SubmitCommand = new AsyncRelayCommand(() => RunAsync(SubmitAsync), () => !IsBusy && ShowSubmit, HandleError);
        ResendCommand = new AsyncRelayCommand(() => RunAsync(ResendAsync), () => !IsBusy, HandleError);
        LogoutCommand = new AsyncRelayCommand(() => RunAsync(LogoutAsync), () => !IsBusy, HandleError);
        CheckSessionCommand = new AsyncRelayCommand(() => RunAsync(CheckSessionAsync), () => !IsBusy, HandleError);
        NavigateCommand = new RelayCommand(Navigate, () => !IsBusy);
        _authentication.SessionChanged += OnSessionChanged;
    }

    public AsyncRelayCommand SubmitCommand
    {
        get;
    }
    public AsyncRelayCommand ResendCommand
    {
        get;
    }
    public AsyncRelayCommand LogoutCommand
    {
        get;
    }
    public AsyncRelayCommand CheckSessionCommand
    {
        get;
    }
    public RelayCommand NavigateCommand
    {
        get;
    }

    public string Email
    {
        get => _email; set => SetProperty(ref _email, value);
    }
    public string Password
    {
        get => _password; set => SetProperty(ref _password, value);
    }
    public string PasswordConfirmation
    {
        get => _passwordConfirmation;
        set => SetProperty(ref _passwordConfirmation, value);
    }
    public string Code
    {
        get => _code; set => SetProperty(ref _code, value);
    }
    public string Message
    {
        get => _message; private set => SetProperty(ref _message, value);
    }
    public bool IsError
    {
        get => _isError; private set => SetProperty(ref _isError, value);
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            SetProperty(ref _isBusy, value);
            OnPropertyChanged(nameof(IsIdle));
            NotifyCommands();
        }
    }
    public bool IsIdle => !IsBusy;
    public AuthScreen Screen
    {
        get => _screen;
        private set
        {
            _screen = value;
            foreach (var name in new[] { nameof(Screen), nameof(Title), nameof(Description), nameof(SubmitLabel),
                nameof(ShowEmail), nameof(ShowPassword), nameof(ShowPasswordConfirmation), nameof(ShowCode),
                nameof(ShowSubmit), nameof(IsLogin), nameof(IsReset), nameof(IsRecovery),
                nameof(IsRestricted), nameof(IsAdministration), nameof(ShowBack) })
            {
                OnPropertyChanged(name);
            }
            NotifyCommands();
        }
    }

    public string Title => Screen switch
    {
        AuthScreen.RequestRecovery => "Recuperar acceso",
        AuthScreen.ResetPassword => "Restablecer contraseña",
        AuthScreen.Restricted => "Acceso restringido",
        AuthScreen.Administration => "Acceso verificado",
        _ => "Acceso de administrador"
    };
    public string Description => Screen switch
    {
        AuthScreen.RequestRecovery => "Si la cuenta puede recibir correos de recuperación, recibirás un código.",
        AuthScreen.ResetPassword => "Usa el código de recuperación y una contraseña de al menos 8 caracteres.",
        AuthScreen.Restricted => "Esta cuenta no tiene permisos de administrador. Usa una cuenta autorizada.",
        AuthScreen.Administration => "Tu cuenta de administrador está autenticada. "
            + "La sesión se renueva mientras la aplicación está abierta.",
        _ => "Inicia sesión con una cuenta autorizada para administrar Cluster JAM."
    };
    public string SubmitLabel => Screen switch
    {
        AuthScreen.RequestRecovery => "Solicitar código",
        AuthScreen.ResetPassword => "Guardar contraseña",
        _ => "Entrar al panel"
    };
    public bool IsLogin => Screen == AuthScreen.Login;
    public bool IsReset => Screen == AuthScreen.ResetPassword;
    public bool IsRecovery => Screen == AuthScreen.RequestRecovery;
    public bool IsRestricted => Screen == AuthScreen.Restricted;
    public bool IsAdministration => Screen == AuthScreen.Administration;
    public bool ShowEmail => !IsRestricted && !IsAdministration;
    public bool ShowPassword => IsLogin || IsReset;
    public bool ShowPasswordConfirmation => IsReset;
    public bool ShowCode => IsReset;
    public bool ShowSubmit => !IsRestricted && !IsAdministration;
    public bool ShowBack => !IsLogin && !IsRestricted && !IsAdministration;
    public string SignedInEmail => _authentication.Identity?.Email ?? "";

    private async Task SubmitAsync()
    {
        switch (Screen)
        {
            case AuthScreen.Login:
                await _authentication.LoginAsync(Email, Password, _lifetime.Token);
                ShowAuthenticatedScreen();
                break;
            case AuthScreen.RequestRecovery:
                await _authentication.RequestRecoveryAsync(Email, _lifetime.Token);
                Screen = AuthScreen.ResetPassword;
                Message = "Si la cuenta puede recibir correos de recuperación, recibirás un código. "
                    + "Revisa también correo no deseado.";
                break;
            case AuthScreen.ResetPassword:
                ValidateConfirmation();
                await _authentication.RecoverPasswordAsync(Email, Code, Password, _lifetime.Token);
                Screen = AuthScreen.Login;
                Message = "Contraseña actualizada. Inicia sesión con tu nueva contraseña.";
                break;
        }
    }

    private async Task ResendAsync()
    {
        await _authentication.RequestRecoveryAsync(Email, _lifetime.Token);
        Message = "Si la cuenta puede recibir correos de recuperación, recibirás otro código.";
        Code = "";
    }

    private async Task LogoutAsync()
    {
        try
        {
            await _authentication.LogoutAsync(_lifetime.Token);
            Message = "Sesión cerrada.";
        }
        catch (ApiException)
        {
            IsError = true;
            Message = "Se cerró la sesión en este equipo. No se pudo confirmar la revocación en el servidor.";
        }
        finally { Screen = AuthScreen.Login; }
    }

    private async Task CheckSessionAsync()
    {
        await _authentication.CheckIdentityAsync(_lifetime.Token);
        ShowAuthenticatedScreen();
        Message = "Identidad y permisos verificados con el servidor.";
    }

    public async Task MaintainSessionAsync()
    {
        if (IsBusy || _authentication.Identity is null)
        {
            return;
        }
        await RunAsync(async () =>
        {
            try
            {
                await _authentication.GetAccessTokenAsync(_lifetime.Token);
            }
            catch (ApiException error) { HandleError(error); }
        });
    }

    private void Navigate(object? parameter)
    {
        if (!Enum.TryParse<AuthScreen>(parameter?.ToString(), out var destination)
            || destination is not (AuthScreen.Login or AuthScreen.RequestRecovery or AuthScreen.ResetPassword))
        {
            return;
        }
        Screen = destination;
        Message = "";
        IsError = false;
        ClearSecrets();
    }

    private void ValidateConfirmation()
    {
        if (Password != PasswordConfirmation)
        {
            throw new ArgumentException("Las contraseñas no coinciden.");
        }
    }

    private async Task RunAsync(Func<Task> operation)
    {
        IsBusy = true;
        Message = "";
        IsError = false;
        try
        {
            await operation();
        }
        finally
        {
            ClearSecrets();
            IsBusy = false;
        }
    }

    private void ShowAuthenticatedScreen() => Screen = _authentication.Identity?.IsAdministrator == true
        ? AuthScreen.Administration : AuthScreen.Restricted;

    private void OnSessionChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(SignedInEmail));
        if (_authentication.Identity is null && (IsAdministration || IsRestricted))
        {
            Screen = AuthScreen.Login;
        }
        else if (_authentication.Identity is not null && (IsAdministration || IsRestricted))
        {
            ShowAuthenticatedScreen();
        }
    }

    private void HandleError(Exception error)
    {
        if (error is OperationCanceledException && _lifetime.IsCancellationRequested)
        {
            return;
        }
        IsError = true;
        Message = error switch
        {
            ArgumentException validation => validation.Message,
            ApiException api => AuthErrorMessages.For(api),
            _ => "Ocurrió un error inesperado. Cierra la aplicación y vuelve a intentar."
        };
        if (IsReset && error is ApiException)
        {
            Message += " El código pudo haberse consumido. Solicita otro antes de intentar de nuevo.";
        }
        if (error is ApiException { Status: 401 } && _authentication.Identity is null
                 && (IsAdministration || IsRestricted))
        {
            Screen = AuthScreen.Login;
        }
    }

    private void ClearSecrets()
    {
        Password = "";
        PasswordConfirmation = "";
        Code = "";
    }

    private void NotifyCommands()
    {
        SubmitCommand.NotifyCanExecuteChanged();
        ResendCommand.NotifyCanExecuteChanged();
        LogoutCommand.NotifyCanExecuteChanged();
        CheckSessionCommand.NotifyCanExecuteChanged();
        NavigateCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _authentication.SessionChanged -= OnSessionChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
        ClearSecrets();
    }
}
