using AutoPresser.Input;
using AutoPresser.Models;
using System.Diagnostics;
using System.Windows.Input;

namespace AutoPresser.Services;

public sealed class BindingManager : IDisposable
{
    private readonly Dictionary<Guid, BindingState> _states = [];
    private readonly Lock _sync = new();
    private readonly InputStateManager _inputState;
    private readonly BindingDiagnostics _diagnostics;
    private bool _disposed;
    private bool _masterEnabled;
    private bool _isEditing;

    public BindingManager(InputSimulator inputSimulator, BindingDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(inputSimulator);
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _inputState = new InputStateManager(inputSimulator);
    }

    public void SetBindings(List<BindingConfiguration> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        BindingValidator.EnsureValid(bindings);
        var configurations = bindings.ToDictionary(binding => binding.Id);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Replacing a configuration is a lifecycle boundary: stop all current
            // actions before their mutable configuration objects can change meaning.
            StopAllLocked();

            foreach (var id in _states.Keys.Where(id => !configurations.ContainsKey(id)).ToArray())
            {
                _states.Remove(id);
            }

            foreach (var configuration in configurations.Values)
            {
                if (_states.TryGetValue(configuration.Id, out var state))
                {
                    state.Configuration = configuration;
                }
                else
                {
                    _states.Add(configuration.Id, new BindingState(configuration));
                }
            }
        }
    }

    /// <summary>Gets whether ordinary bindings are currently allowed to start.</summary>
    public bool IsMasterEnabled
    {
        get
        {
            lock (_sync)
            {
                return _masterEnabled;
            }
        }
    }

    /// <summary>Gets whether binding editing is suspending all actions.</summary>
    public bool IsEditing
    {
        get
        {
            lock (_sync)
            {
                return _isEditing;
            }
        }
    }

    /// <summary>
    /// Enables or disables ordinary bindings. The manager always starts disabled,
    /// and enabling is refused while editing is suspended.
    /// </summary>
    public bool SetMasterEnabled(bool enabled)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (enabled && _isEditing)
            {
                _diagnostics.Log("Cannot enable bindings while editing is suspended.");
                return false;
            }

            _masterEnabled = enabled;
            if (!enabled)
            {
                StopAllLocked();
                _diagnostics.Log("Master switch disabled; all active bindings stopped.");
            }
            else
            {
                _diagnostics.Log("Master switch enabled.");
            }

            return true;
        }
    }

    /// <summary>
    /// Suspends binding execution while editing. Entering edit mode stops all
    /// actions and forces the master switch off. Leaving edit mode does not
    /// automatically re-enable bindings.
    /// </summary>
    public void SetEditing(bool editing)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_isEditing == editing)
            {
                return;
            }

            _isEditing = editing;
            if (editing)
            {
                _masterEnabled = false;
                StopAllLocked();
                _diagnostics.Log("Editing mode entered; bindings suspended and all inputs released.");
            }
            else
            {
                _diagnostics.Log("Editing mode ended; master switch remains disabled until explicitly enabled.");
            }
        }
    }

    /// <summary>Stops all actions and disables ordinary bindings immediately.</summary>
    public void EmergencyStop()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _masterEnabled = false;
            StopAllLocked();
            _diagnostics.Log("Emergency stop completed; master switch disabled.");
        }
    }

    public void Toggle(Key trigger, TriggerModifiers modifiers = TriggerModifiers.None)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_masterEnabled || _isEditing)
            {
                return;
            }

            var matches = _states.Values
                .Where(state => state.Configuration.IsEnabled &&
                                state.Configuration.Trigger == trigger &&
                                state.Configuration.Modifiers == modifiers)
                .ToArray();

            foreach (var state in matches)
            {
                if (state.IsRunning)
                {
                    StopLocked(state);
                }
                else
                {
                    StartLocked(state);
                }
            }
        }
    }

    public void StopAll()
    {
        lock (_sync)
        {
            StopAllLocked();
        }
    }

    private void StartLocked(BindingState state)
    {
        var configuration = state.Configuration;
        var generation = ++state.Generation;
        state.IsRunning = true;

        _diagnostics.Log($"Starting binding with ID: {configuration.Id}");

        if (configuration.Action == BindingAction.Hold)
        {
            try
            {
                _inputState.Press(configuration.Target);
                state.HeldTarget = configuration.Target;
                _diagnostics.Log($"Holding {DescribeTarget(configuration.Target)}");
            }
            catch
            {
                state.IsRunning = false;
                throw;
            }

            return;
        }

        var cancellation = new CancellationTokenSource();
        state.Cancellation = cancellation;

        // Run on a worker so the first iteration cannot execute synchronously
        // while StartLocked still owns _sync.
        state.Worker = Task.Run(() => RunAutoClickAsync(
            state,
            generation,
            configuration,
            cancellation), cancellation.Token);
    }

    private async Task RunAutoClickAsync(
        BindingState state,
        int generation,
        BindingConfiguration configuration,
        CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;

        try
        {
            var interval = Math.Max(1, configuration.IntervalMilliseconds);
            var repeatCount = Math.Max(0, configuration.RepeatCount);
            var completed = 0;

            _diagnostics.Log(
                $"Running Auto Click: {DescribeTarget(configuration.Target)}, " +
                $"interval {interval} ms, repeat count {repeatCount} (0 = infinite).");

            while (!token.IsCancellationRequested && (repeatCount == 0 || completed < repeatCount))
            {
                lock (_sync)
                {
                    // Stop/restart invalidates this generation. Checking and sending
                    // the click under the same lock prevents a click after StopAll.
                    if (_disposed || !state.IsRunning || state.Generation != generation ||
                        !ReferenceEquals(state.Cancellation, cancellation) || token.IsCancellationRequested)
                    {
                        break;
                    }

                    _inputState.Click(configuration.Target);
                }

                completed++;

                if (repeatCount != 0 && completed >= repeatCount)
                {
                    break;
                }

                await Task.Delay(interval, token).ConfigureAwait(false);
            }

            if (!token.IsCancellationRequested)
            {
                _diagnostics.Log($"Auto Click completed for binding {configuration.Id}.");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Expected when toggled off, suspended, or shut down.
        }
        catch (Exception exception)
        {
            _diagnostics.Log($"Auto Click failed for binding {configuration.Id}: {exception.Message}");
            Debug.WriteLine($"[AutoPresser] Auto Click failure: {exception}");

            lock (_sync)
            {
                // A failed click may have left an input held. Stop every action
                // before releasing everything, so another worker cannot keep
                // sending input after the safety cleanup.
                EmergencyCleanupLocked();
            }
        }
        finally
        {
            lock (_sync)
            {
                // An older worker must never mutate state belonging to a restart.
                if (state.Generation == generation && ReferenceEquals(state.Cancellation, cancellation))
                {
                    state.IsRunning = false;
                    state.Cancellation = null;
                    state.Worker = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void StopLocked(BindingState state)
    {
        ++state.Generation;
        state.IsRunning = false;

        var cancellation = state.Cancellation;
        state.Cancellation = null;
        cancellation?.Cancel();
        // The worker owns disposal of its CancellationTokenSource.
        state.Worker = null;

        if (state.HeldTarget is not null)
        {
            var target = state.HeldTarget;
            state.HeldTarget = null;

            try
            {
                _inputState.Release(target);
                _diagnostics.Log($"Released {DescribeTarget(target)} for binding {state.Configuration.Id}.");
            }
            catch (Exception exception)
            {
                _diagnostics.Log($"Failed to release {DescribeTarget(target)}: {exception.Message}");
                // A failed key-up is a safety event: stop other actions as well.
                EmergencyCleanupLocked();
            }
        }

        _diagnostics.Log($"Stopped binding with ID: {state.Configuration.Id}");
    }

    private void StopAllLocked()
    {
        foreach (var state in _states.Values.ToArray())
        {
            StopLocked(state);
        }

        // Final safety net for inputs left owned by an interrupted or failed click.
        ReleaseAllInputsLocked();
    }

    private void EmergencyCleanupLocked()
    {
        foreach (var state in _states.Values)
        {
            ++state.Generation;
            state.IsRunning = false;

            var cancellation = state.Cancellation;
            state.Cancellation = null;
            cancellation?.Cancel();
            state.Worker = null;
            state.HeldTarget = null;
        }

        ReleaseAllInputsLocked();
    }

    private void ReleaseAllInputsLocked()
    {
        foreach (var exception in _inputState.ReleaseAll())
        {
            _diagnostics.Log($"Input release failed during cleanup: {exception.Message}");
            Debug.WriteLine($"[AutoPresser] Input release failure: {exception}");
        }

        foreach (var state in _states.Values)
        {
            state.HeldTarget = null;
        }
    }

    private static string DescribeTarget(InputTarget target) => target.Device switch
    {
        TargetDevice.Keyboard => $"'{target.Key}' key",
        TargetDevice.Mouse => $"'{target.MouseButton}' mouse button",
        _ => "unknown target"
    };

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            // Prevent workers and hook callbacks from starting new work before cleanup.
            _disposed = true;
            _masterEnabled = false;
            _isEditing = true;
            StopAllLocked();
        }
    }

    private sealed class BindingState(BindingConfiguration configuration)
    {
        public BindingConfiguration Configuration { get; set; } = configuration;
        public bool IsRunning { get; set; }
        public int Generation { get; set; }
        public InputTarget? HeldTarget { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public Task? Worker { get; set; }
    }
}
