using System.Diagnostics;

namespace AutoPresser.Services;

public sealed class BindingDiagnostics
{
    public event EventHandler<BindingDiagnosticEventArgs>? MessageLogged;

    public bool DebugEnabled { get; set; } = true;

    public void Log(string message)
    {
        switch (DebugEnabled)
        {
            case true:
                Debug.WriteLine($"[AutoPresser] {message}");
                break;
            // Avoid formatting or dispatching diagnostic events when
            // there are no listeners and debug logging is disabled.
            case false when MessageLogged is null:
                return;
        }

        MessageLogged?.Invoke(
            this,
            new BindingDiagnosticEventArgs(DateTime.Now, message));
    }
}

public sealed class BindingDiagnosticEventArgs(
    DateTime timestamp,
    string message) : EventArgs
{
    public DateTime Timestamp { get; } = timestamp;

    public string Message { get; } = message;
}