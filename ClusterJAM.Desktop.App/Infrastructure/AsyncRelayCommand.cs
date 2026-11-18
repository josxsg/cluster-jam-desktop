using System.Windows.Input;

namespace ClusterJAM.Desktop.App.Infrastructure;

public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute, Action<Exception> onError) : ICommand
{
    private bool _isExecuting;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_isExecuting && canExecute();

    // ICommand is an event boundary; all exceptions are handled here.
    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }
        _isExecuting = true;
        NotifyCanExecuteChanged();
        try
        {
            await execute();
        }
        catch (Exception error) { onError(error); }
        finally
        {
            _isExecuting = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
